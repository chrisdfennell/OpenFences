using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace OpenFences.Services
{
    /// <summary>A newer release found on GitHub.</summary>
    internal sealed record UpdateInfo(
        Version Version,
        string Tag,
        string Notes,
        string ReleasePageUrl,
        string? MsiUrl,        // null when this release has no MSI for our architecture
        string? MsiSha256);    // from GitHub's asset digest, when available

    /// <summary>
    /// Checks GitHub Releases for a newer OpenFences and installs it. Installing downloads the
    /// MSI for this PC's architecture, verifies it, then hands off to a small helper that waits
    /// for OpenFences to exit, runs the installer (the MSI's MajorUpgrade replaces the old
    /// version in place), and starts OpenFences again.
    /// </summary>
    internal static class UpdateService
    {
        private const string Repo = "chrisdfennell/OpenFences";
        private const string LatestReleaseApi = "https://api.github.com/repos/" + Repo + "/releases/latest";

        private static readonly HttpClient Http = CreateClient();

        private static HttpClient CreateClient()
        {
            var http = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("OpenFences-Updater/" + CurrentVersion);
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            return http;
        }

        public static Version CurrentVersion
        {
            get
            {
                var asm = Assembly.GetExecutingAssembly();
                var info = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
                return TryParseVersion(info, out var v) ? v : (asm.GetName().Version ?? new Version(0, 0));
            }
        }

        /// <summary>"x64" or "arm64": matches the OpenFences-{arch}.msi release assets.</summary>
        private static string Arch =>
            RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "arm64" : "x64";

        /// <summary>True when running from an MSI install (Program Files), as opposed to the
        /// portable zip, which can't be upgraded by the installer.</summary>
        public static bool IsInstalledCopy
        {
            get
            {
                var dir = AppContext.BaseDirectory;
                return new[]
                {
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                    Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                }
                .Where(p => !string.IsNullOrEmpty(p))
                .Any(p => dir.StartsWith(p.TrimEnd('\\') + "\\", StringComparison.OrdinalIgnoreCase));
            }
        }

        /// <summary>Returns the latest release if it's newer than this build, otherwise null.
        /// Throws on network/API errors so a manual check can report them.</summary>
        public static async Task<UpdateInfo?> CheckAsync(CancellationToken ct = default)
        {
            using var resp = await Http.GetAsync(LatestReleaseApi, ct).ConfigureAwait(false);
            resp.EnsureSuccessStatusCode();
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            var root = doc.RootElement;

            if (root.TryGetProperty("draft", out var draft) && draft.GetBoolean()) return null;
            if (root.TryGetProperty("prerelease", out var pre) && pre.GetBoolean()) return null;

            string tag = root.GetProperty("tag_name").GetString() ?? "";
            if (!TryParseVersion(tag, out var latest) || latest <= CurrentVersion) return null;

            string? msiUrl = null, sha = null;
            string wanted = $"OpenFences-{Arch}.msi";
            if (root.TryGetProperty("assets", out var assets))
            {
                foreach (var a in assets.EnumerateArray())
                {
                    if (!string.Equals(a.GetProperty("name").GetString(), wanted, StringComparison.OrdinalIgnoreCase))
                        continue;
                    msiUrl = a.GetProperty("browser_download_url").GetString();
                    if (a.TryGetProperty("digest", out var d) && d.ValueKind == JsonValueKind.String &&
                        d.GetString() is string digest && digest.StartsWith("sha256:", StringComparison.OrdinalIgnoreCase))
                        sha = digest.Substring("sha256:".Length);
                    break;
                }
            }

            return new UpdateInfo(
                latest,
                tag,
                root.TryGetProperty("body", out var body) ? body.GetString() ?? "" : "",
                root.TryGetProperty("html_url", out var page) ? page.GetString() ?? "" : $"https://github.com/{Repo}/releases",
                msiUrl,
                sha);
        }

        /// <summary>Downloads and verifies the MSI. Returns its local path.</summary>
        public static async Task<string> DownloadAsync(UpdateInfo update, IProgress<double>? progress, CancellationToken ct = default)
        {
            if (update.MsiUrl == null) throw new InvalidOperationException("This release has no installer for this PC.");

            var dir = Path.Combine(Path.GetTempPath(), "OpenFences-Update");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, $"OpenFences-{update.Version.ToString(3)}-{Arch}.msi");

            using (var resp = await Http.GetAsync(update.MsiUrl, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
            {
                resp.EnsureSuccessStatusCode();
                long? total = resp.Content.Headers.ContentLength;
                await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
                await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
                var buf = new byte[81920];
                long done = 0;
                int n;
                while ((n = await src.ReadAsync(buf, ct).ConfigureAwait(false)) > 0)
                {
                    await dst.WriteAsync(buf.AsMemory(0, n), ct).ConfigureAwait(false);
                    done += n;
                    if (total > 0) progress?.Report((double)done / total.Value);
                }
            }

            if (update.MsiSha256 != null)
            {
                string actual;
                await using (var fs = File.OpenRead(path))
                    actual = Convert.ToHexString(await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false));
                if (!actual.Equals(update.MsiSha256, StringComparison.OrdinalIgnoreCase))
                {
                    try { File.Delete(path); } catch { /* ignore */ }
                    throw new InvalidDataException("The downloaded update didn't match its published checksum, so it wasn't installed.");
                }
            }

            return path;
        }

        /// <summary>
        /// Starts a hidden helper that waits for this process to exit, runs the installer and
        /// relaunches OpenFences. The caller must then close the app normally (so the config is
        /// saved and the desktop icons are restored). If the user declines the admin prompt the
        /// installer does nothing, and the current version simply starts again.
        /// </summary>
        public static void LaunchInstallerAndRestart(string msiPath)
        {
            string exe = Environment.ProcessPath ?? Path.Combine(AppContext.BaseDirectory, "OpenFences.exe");
            string log = Path.Combine(Path.GetDirectoryName(msiPath)!, "install.log");
            string script = BuildInstallScript(msiPath, exe, Environment.ProcessId, log);

            Process.Start(new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = "-NoProfile -ExecutionPolicy Bypass -WindowStyle Hidden -EncodedCommand " +
                            Convert.ToBase64String(Encoding.Unicode.GetBytes(script)),
                UseShellExecute = false,
                CreateNoWindow = true,
            });
        }

        // Wait for OpenFences to exit (so its files aren't in use), install, then relaunch. The
        // installer reuses the same install folder, so the old exe path is the new exe.
        internal static string BuildInstallScript(string msiPath, string exe, int pid, string log)
        {
            static string Q(string s) => "'" + s.Replace("'", "''") + "'";
            return
                $"try {{ Wait-Process -Id {pid} -Timeout 60 -ErrorAction SilentlyContinue }} catch {{}}\n" +
                $"Start-Process msiexec.exe -ArgumentList @('/i', {Q('"' + msiPath + '"')}, '/passive', '/norestart', '/l*v', {Q('"' + log + '"')}) -Wait\n" +
                $"Start-Process {Q(exe)}\n";
        }

        public static void OpenReleasePage(UpdateInfo update)
        {
            try { Process.Start(new ProcessStartInfo(update.ReleasePageUrl) { UseShellExecute = true }); }
            catch { /* ignore */ }
        }

        private static bool TryParseVersion(string? s, out Version version)
        {
            version = new Version(0, 0);
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Trim().TrimStart('v', 'V');
            int cut = s.IndexOfAny(new[] { '+', '-' }); // drop "+commit" / "-beta" suffixes
            if (cut >= 0) s = s[..cut];
            if (!Version.TryParse(s, out var v)) return false;
            // Normalize so 1.1.1 == 1.1.1.0 (Version treats missing parts as -1).
            version = new Version(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
            return true;
        }
    }
}
