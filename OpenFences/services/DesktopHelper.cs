using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Interop;

namespace OpenFences
{
    internal static class DesktopHelper
    {
        // ---- Messages / commands ----
        private const int WM_COMMAND = 0x0111;
        private const int CMD_TOGGLE_DESKTOP = 0x7402;     // "Show desktop icons" verb
        private const int LVM_HITTEST = 0x1012;

        // ShowWindow
        private const int SW_HIDE = 0;
        private const int SW_SHOW = 5;

        // SetWindowPos
        private static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSENDCHANGING = 0x0400;
        private const uint SWP_SHOWWINDOW = 0x0040;

        // Cached handles to the desktop hierarchy
        private static IntPtr _progman = IntPtr.Zero;   // "Progman"
        private static IntPtr _workerW = IntPtr.Zero;   // WorkerW that hosts the desktop
        private static IntPtr _defView = IntPtr.Zero;   // "SHELLDLL_DefView"
        private static IntPtr _listView = IntPtr.Zero;  // "SysListView32" (desktop icons)

        // ---------- Public API ----------

        /// <summary>Call once on startup (we re-ensure as needed).</summary>
        public static void InitializeDesktopHandles()
        {
            // Ask Progman to create WorkerWs (for builds that use WorkerW).
            _progman = FindWindow("Progman", "Program Manager");
            if (_progman != IntPtr.Zero)
                SendMessageTimeout(_progman, 0x052C, IntPtr.Zero, IntPtr.Zero, 0, 1000, out _);

            RefreshHandles();
        }

        /// <summary>
        /// Robust toggle: reads current visibility, asks shell to toggle if needed (non-blocking), then verifies/forces state.
        /// </summary>
        public static void ToggleDesktopIconsRobust()
        {
            SetDesktopIconsVisible(!AreIconsVisible());
        }

        /// <summary>
        /// Ensures icons are visible/hidden. Uses PostMessage to avoid blocking Explorer; verifies after a short delay.
        /// </summary>
        public static void SetDesktopIconsVisible(bool show)
        {
            EnsureHandles();
            bool before = AreIconsVisible();

            // 1) Asynchronously ask shell to toggle if needed (non-blocking)
            if (before != show && _defView != IntPtr.Zero)
            {
                // Was SendMessage(...); PostMessage avoids Explorer stalls/lag
                PostMessage(_defView, WM_COMMAND, new IntPtr(CMD_TOGGLE_DESKTOP), IntPtr.Zero);
            }

            // 2) After a short delay, verify and force the desired state only if still wrong
            var dispatcher = System.Windows.Application.Current?.Dispatcher;
            if (dispatcher is null) return;

            var timer = new System.Windows.Threading.DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(120) // time for the shell to process the command
            };
            timer.Tick += (s, e) =>
            {
                try
                {
                    timer.Stop();
                    EnsureHandles();
                    bool now = AreIconsVisible();

                    if (now != show && _listView != IntPtr.Zero)
                    {
                        // Force final state as a fallback (rare)
                        ShowWindow(_listView, show ? SW_SHOW : SW_HIDE);
                    }
                }
                catch { /* ignore */ }
            };
            timer.Start();
        }

        // Legacy names kept for callers — single definitions only.
        public static void ToggleDesktopIcons() => ToggleDesktopIconsRobust();
        public static void ShowDesktopIcons(bool visible) => SetDesktopIconsVisible(visible);

        /// <summary>True if the desktop icon list view is visible.</summary>
        public static bool AreIconsVisible()
        {
            EnsureHandles();
            return _listView != IntPtr.Zero && IsWindowVisible(_listView);
        }

        /// <summary>
        /// Returns true when the cursor is over the "empty desktop" (not our windows, not a desktop icon item).
        /// </summary>
        public static bool CursorIsOverEmptyDesktop()
        {
            EnsureHandles();

            if (_defView == IntPtr.Zero) return false;
            if (!GetCursorPos(out POINT ptScreen)) return false;

            // If pointer is on one of OUR windows, it's not the desktop.
            IntPtr hwndUnder = WindowFromPoint(ptScreen);
            if (hwndUnder == IntPtr.Zero) return false;
            if (BelongsToCurrentProcess(hwndUnder)) return false;

            // Accept clicks that land on WorkerW/Progman directly…
            if (IsClass(hwndUnder, "WorkerW") || IsClass(hwndUnder, "Progman")) return true;

            // …or on anything hosted under SHELLDLL_DefView (the desktop view).
            if (AncestorHasClass(hwndUnder, "SHELLDLL_DefView")) return true;

            return false;
        }

        /// <summary>
        /// Push a window to the "desktop layer": above the wallpaper & icons (WorkerW/Progman), below normal apps.
        /// </summary>
        public static void SendToDesktopLayer(Window window)
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;
            SendToDesktopLayer(hwnd);
        }

        public static void SendToDesktopLayer(IntPtr hwnd)
        {
            EnsureHandles();

            // Place directly above the WorkerW (or Progman if no WorkerW).
            IntPtr insertAfter = (_workerW != IntPtr.Zero) ? _workerW : _progman;
            if (insertAfter == IntPtr.Zero) return;

            SetWindowPos(hwnd, insertAfter, 0, 0, 0, 0,
                SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE | SWP_NOOWNERZORDER | SWP_NOSENDCHANGING | SWP_SHOWWINDOW);
        }

        // ---------- internals ----------

        private static void EnsureHandles()
        {
            if (_progman == IntPtr.Zero || !IsWindow(_progman))
                _progman = FindWindow("Progman", "Program Manager");

            if (_workerW == IntPtr.Zero || !IsWindow(_workerW) ||
                _defView == IntPtr.Zero || !IsWindow(_defView) ||
                _listView == IntPtr.Zero || !IsWindow(_listView))
            {
                RefreshHandles();
            }
        }

        private static void RefreshHandles()
        {
            _defView = IntPtr.Zero;
            _workerW = IntPtr.Zero;
            _listView = IntPtr.Zero;

            // First, try WorkerW → DefView
            EnumWindows((hwnd, l) =>
            {
                if (IsClass(hwnd, "WorkerW"))
                {
                    var def = FindChildByClass(hwnd, "SHELLDLL_DefView");
                    if (def != IntPtr.Zero)
                    {
                        _workerW = hwnd;
                        _defView = def;
                        return false; // stop
                    }
                }
                return true;
            }, IntPtr.Zero);

            // Some builds host DefView directly under Progman
            if (_defView == IntPtr.Zero)
            {
                _progman = (_progman == IntPtr.Zero || !IsWindow(_progman))
                    ? FindWindow("Progman", "Program Manager")
                    : _progman;

                if (_progman != IntPtr.Zero)
                {
                    var def = FindChildByClass(_progman, "SHELLDLL_DefView");
                    if (def != IntPtr.Zero)
                    {
                        _defView = def;
                        _workerW = _progman;
                    }
                }
            }

            if (_defView != IntPtr.Zero)
                _listView = FindWindowEx(_defView, IntPtr.Zero, "SysListView32", null);
        }

        private static bool BelongsToCurrentProcess(IntPtr hwnd)
        {
            GetWindowThreadProcessId(hwnd, out uint pid);
            return pid == (uint)Process.GetCurrentProcess().Id;
        }

        private static bool IsClass(IntPtr hwnd, string className)
        {
            var sb = new StringBuilder(256);
            GetClassName(hwnd, sb, sb.Capacity);
            return sb.ToString().Equals(className, StringComparison.Ordinal);
        }

        private static bool AncestorHasClass(IntPtr hwnd, string className)
        {
            IntPtr cur = hwnd;
            while (cur != IntPtr.Zero)
            {
                if (IsClass(cur, className)) return true;
                cur = GetParent(cur);
            }
            return false;
        }

        private static IntPtr FindChildByClass(IntPtr parent, string className)
        {
            IntPtr result = IntPtr.Zero;
            EnumChildWindows(parent, (h, l) =>
            {
                if (IsClass(h, className))
                {
                    result = h;
                    return false;
                }
                return true;
            }, IntPtr.Zero);
            return result;
        }

        // ---------- P/Invoke ----------

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindow(string lpClassName, string lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool EnumChildWindows(IntPtr hWndParent, EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SendMessage(IntPtr hWnd, int msg, IntPtr wParam, ref LVHITTESTINFO lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SendMessageTimeout(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam, int fuFlags, int uTimeout, out IntPtr lpdwResult);

        [DllImport("user32.dll")]
        private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool IsWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern bool GetCursorPos(out POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern bool ScreenToClient(IntPtr hWnd, ref POINT lpPoint);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT Point);

        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetParent(IntPtr hWnd);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        // structs
        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct LVHITTESTINFO
        {
            public POINT pt;
            public uint flags;
            public int iItem;
            public int iSubItem;
        }
    }
}