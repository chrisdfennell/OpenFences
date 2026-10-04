using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;

namespace Pickets.Services
{
    /// <summary>
    /// "Close all": finds the running apps behind a fence's app tiles (.exe files and shortcuts to
    /// them) and asks their windows to close, exactly like clicking each window's X, so apps still
    /// offer to save unsaved work. Nothing is force-killed. Documents, folders and shell items are
    /// skipped: closing Word for one .docx would close every other document too.
    /// </summary>
    internal static class AppCloser
    {
        /// <summary>A running app matched to a tile: its label, program and open windows.</summary>
        public sealed record RunningApp(string Name, string ExePath, IReadOnlyList<IntPtr> Windows);

        // Never close these from a fence: Explorer also draws the desktop and taskbar.
        private static readonly string[] Protected = { "explorer.exe" };

        /// <summary>The program a tile starts, or null if it isn't an app tile.</summary>
        public static string? ExeForTile(string path, Func<string, string?> shortcutTarget)
        {
            if (string.IsNullOrWhiteSpace(path) || path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                return null;
            string ext = Path.GetExtension(path);
            string? exe = ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ? path
                        : ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ? shortcutTarget(path)
                        : null;
            if (exe == null || !Path.GetExtension(exe).Equals(".exe", StringComparison.OrdinalIgnoreCase)) return null;
            if (Protected.Contains(Path.GetFileName(exe), StringComparer.OrdinalIgnoreCase)) return null;
            return exe;
        }

        /// <summary>
        /// Pairs tiles with open windows by program path. One entry per program, named after the
        /// first tile that starts it, in tile order; programs with no open window are left out.
        /// </summary>
        public static List<RunningApp> Match(IEnumerable<string> tilePaths,
                                             Func<string, string?> shortcutTarget,
                                             IEnumerable<(IntPtr Hwnd, string ExePath)> windows,
                                             string? ownExe = null)
        {
            var byExe = windows.GroupBy(w => Normalize(w.ExePath), StringComparer.OrdinalIgnoreCase)
                               .ToDictionary(g => g.Key, g => g.Select(w => w.Hwnd).ToList(), StringComparer.OrdinalIgnoreCase);
            string? own = ownExe == null ? null : Normalize(ownExe);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<RunningApp>();
            foreach (var tile in tilePaths)
            {
                if (ExeForTile(tile, shortcutTarget) is not string exe) continue;
                string key = Normalize(exe);
                if (string.Equals(key, own, StringComparison.OrdinalIgnoreCase) || !seen.Add(key)) continue;
                if (byExe.TryGetValue(key, out var hwnds) && hwnds.Count > 0)
                    result.Add(new RunningApp(DesktopItems.LabelFor(tile), exe, hwnds));
            }
            return result;
        }

        /// <summary>The running apps behind these tiles, from the windows open right now.</summary>
        public static List<RunningApp> FindRunning(IEnumerable<string> tilePaths) =>
            Match(tilePaths, SafeShortcutTarget, OpenAppWindows(), Environment.ProcessPath);

        /// <summary>Asks every window of these apps to close (WM_CLOSE). Returns immediately.</summary>
        public static void Close(IEnumerable<RunningApp> apps)
        {
            foreach (var hwnd in apps.SelectMany(a => a.Windows))
                PostMessage(hwnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
        }

        private static string Normalize(string path)
        {
            try { return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'))); }
            catch { return path; }
        }

        private static string? SafeShortcutTarget(string lnk)
        {
            try { return ShellLink.GetShortcutTarget(lnk); } catch { return null; }
        }

        // Windows a person would call "the app": visible, unowned, titled, not tool windows and not
        // cloaked (suspended Store apps and windows on other virtual desktops' hidden copies).
        private static List<(IntPtr, string)> OpenAppWindows()
        {
            var list = new List<(IntPtr, string)>();
            var exeByPid = new Dictionary<uint, string?>();
            EnumWindows((hwnd, _) =>
            {
                if (!IsWindowVisible(hwnd) || GetWindow(hwnd, GW_OWNER) != IntPtr.Zero) return true;
                if (GetWindowTextLength(hwnd) == 0) return true;
                if ((GetWindowLongPtr(hwnd, GWL_EXSTYLE).ToInt64() & WS_EX_TOOLWINDOW) != 0) return true;
                if (DwmGetWindowAttribute(hwnd, DWMWA_CLOAKED, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return true;
                GetWindowThreadProcessId(hwnd, out uint pid);
                if (!exeByPid.TryGetValue(pid, out var exe)) exeByPid[pid] = exe = ProcessExe(pid);
                if (exe != null) list.Add((hwnd, exe));
                return true;
            }, IntPtr.Zero);
            return list;
        }

        // Null for processes we may not inspect (elevated apps when Pickets isn't), so they're skipped.
        private static string? ProcessExe(uint pid)
        {
            IntPtr h = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
            if (h == IntPtr.Zero) return null;
            try
            {
                var sb = new StringBuilder(1024);
                int size = sb.Capacity;
                return QueryFullProcessImageName(h, 0, sb, ref size) ? sb.ToString(0, size) : null;
            }
            finally { CloseHandle(h); }
        }

        private const uint WM_CLOSE = 0x0010;
        private const uint GW_OWNER = 4;
        private const int GWL_EXSTYLE = -20;
        private const long WS_EX_TOOLWINDOW = 0x80;
        private const int DWMWA_CLOAKED = 14;
        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

        [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
        [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
        [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hwnd, uint cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextLength(IntPtr hwnd);
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr hwnd, int index);
        [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
        [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hwnd, uint msg, IntPtr w, IntPtr l);
        [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr hwnd, int attr, out int value, int size);
        [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
        [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
        [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr h, int flags, StringBuilder name, ref int size);
    }
}
