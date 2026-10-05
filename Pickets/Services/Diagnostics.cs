using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

using WinForms = System.Windows.Forms;

namespace Pickets.Services
{
    /// <summary>
    /// About → Copy diagnostics: a plain-text summary for bug reports. Version, Windows, monitors,
    /// how many fences of which kind, the settings that change behavior, and the end of the error
    /// log. Fence and file names aren't included, and the user's name, profile folder and PC name
    /// are replaced in what is (log lines can contain paths).
    /// </summary>
    internal static class Diagnostics
    {
        private const int LogLines = 40;

        public static string Build(AppConfig config, string logPath)
        {
            var sb = new StringBuilder();
            var o = config.Options;
            var fences = config.Fences;
            var real = fences.Where(f => f.HoldsDesktopItems).ToList();

            sb.AppendLine($"Pickets {UpdateService.CurrentVersion.ToString(3)} ({RuntimeInformation.ProcessArchitecture}, {InstallText})");
            sb.AppendLine($"Windows: {RuntimeInformation.OSDescription} ({RuntimeInformation.OSArchitecture})");
            sb.AppendLine($".NET: {RuntimeInformation.FrameworkDescription}");
            sb.AppendLine($"Theme: {o.Theme} (showing {(Theme.IsLight ? "light" : "dark")}), high contrast: {(System.Windows.SystemParameters.HighContrast ? "on" : "off")}");
            sb.AppendLine();

            sb.AppendLine("Monitors:");
            foreach (var s in WinForms.Screen.AllScreens)
            {
                var b = s.Bounds;
                int scale = (int)Math.Round(ScreenLayout.ScaleAtPx(b.Left + 1, b.Top + 1) * 100);
                sb.AppendLine($"  {b.Width}x{b.Height} at {b.Left},{b.Top}, {scale}%{(s.Primary ? ", primary" : "")}");
            }
            sb.AppendLine();

            sb.AppendLine($"Fences: {real.Count}, portals: {fences.Count(f => f.IsPortal)}, monitors: {fences.Count(f => f.IsMonitor)}, closed: {fences.Count(f => f.Closed)}, " +
                          $"rolled up: {fences.Count(f => f.Collapsed)}, locked: {fences.Count(f => f.Locked)}, " +
                          $"fitted: {fences.Count(f => f.AutoHeight)}");
            sb.AppendLine($"Items in fences: {real.Sum(f => f.ItemPaths.Count)}, fences with tabs: {real.Count(f => f.Tabs.Count >= 2)}, " +
                          $"with backgrounds: {fences.Count(f => !string.IsNullOrWhiteSpace(f.BackgroundMedia))}, glass: {fences.Count(f => f.Glass)}");
            sb.AppendLine($"Profiles: {config.Profiles.Count}, rules: {config.Rules.Count}, auto-organize: {OnOff(o.AutoOrganize)}");
            sb.AppendLine();

            sb.AppendLine("Settings: " + string.Join(", ", new[]
            {
                $"run at startup {OnOff(o.RunAtStartup)}",
                $"snap {OnOff(o.SnapToEdges)}",
                $"grid {OnOff(o.SnapToGrid)}",
                $"stacks {OnOff(o.MoveStacksTogether)}",
                $"one open per stack {OnOff(o.StackOneOpen)}",
                $"hover peek {OnOff(o.ExpandCollapsedOnHover)}",
                $"public IP lookup {OnOff(o.MonitorPublicIp)}",
                $"thumbnails {OnOff(o.ShowThumbnails)}",
                $"hide extensions {OnOff(o.HideFileExtensions)}",
                $"double-click peek {OnOff(o.DoubleClickPeekFences)}",
                $"double-click icons {OnOff(o.DoubleClickDesktopToToggleIcons)}",
                $"hotkeys {OnOff(o.GlobalHotkeys)} ({o.ToggleFencesHotkey}, {o.SearchHotkey}, {o.FrontHotkey}, {o.ProfileHotkey})",
                $"update checks {OnOff(o.CheckForUpdates)}",
            }));
            sb.AppendLine();

            sb.AppendLine($"Error log (last {LogLines} lines):");
            var log = LastLines(logPath, LogLines);
            sb.AppendLine(log.Count == 0 ? "  (empty)" : string.Join(Environment.NewLine, log));

            return Redact(sb.ToString(), Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                          Environment.UserName, Environment.MachineName);
        }

        /// <summary>Replace the profile folder, user name and PC name, ignoring case. Short names
        /// (a user called "a") are only replaced as whole words, so other text survives.</summary>
        public static string Redact(string text, string? userProfile, string? userName, string? machineName)
        {
            if (!string.IsNullOrEmpty(userProfile))
                text = Regex.Replace(text, Regex.Escape(userProfile.TrimEnd('\\')), "%USERPROFILE%", RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(userName))
                text = Regex.Replace(text, $@"(?<![\w.]){Regex.Escape(userName)}(?![\w])", "<user>", RegexOptions.IgnoreCase);
            if (!string.IsNullOrEmpty(machineName))
                text = Regex.Replace(text, $@"(?<![\w.]){Regex.Escape(machineName)}(?![\w])", "<pc>", RegexOptions.IgnoreCase);
            return text;
        }

        private static string OnOff(bool b) => b ? "on" : "off";

        private static string InstallText => UpdateService.Kind switch
        {
            InstallKind.AllUsers => "installed for everyone",
            InstallKind.JustMe => "installed just for this user",
            _ => "portable"
        };

        private static List<string> LastLines(string path, int count)
        {
            try
            {
                if (!File.Exists(path)) return new List<string>();
                var lines = File.ReadAllLines(path);
                return lines.Skip(Math.Max(0, lines.Length - count)).Select(l => "  " + l).ToList();
            }
            catch { return new List<string> { "  (couldn't read the log)" }; }
        }
    }
}
