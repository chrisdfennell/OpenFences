using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace OpenFences
{
    /// <summary>
    /// Low-level mouse hook that detects a real double-click on EMPTY desktop
    /// and toggles desktop icons, without blocking Explorer.
    /// </summary>
    internal static class DesktopDoubleClickMonitor
    {
        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;

        private static IntPtr _hook = IntPtr.Zero;
        private static LowLevelMouseProc? _proc;

        // Double-click detection (use system thresholds)
        private static long _lastDownTick;
        private static POINT _lastDownPt;

        private static int _dblTimeMs = GetDoubleClickTime();
        private static int _dblCx = GetSystemMetrics(SM_CXDOUBLECLK);
        private static int _dblCy = GetSystemMetrics(SM_CYDOUBLECLK);
        private const int SM_CXDOUBLECLK = 36;
        private const int SM_CYDOUBLECLK = 37;

        // Re-entry guard to avoid "toggle twice" flicker while Explorer rebuilds view
        private static bool _guardBusy;
        private static DispatcherTimer? _guardTimer;
        private static readonly TimeSpan GuardInterval = TimeSpan.FromMilliseconds(900);

        // Tiny delay so we don't contend with Explorer's mouse processing
        private static readonly TimeSpan DelayBeforeToggle = TimeSpan.FromMilliseconds(200);

        public static bool IsRunning => _hook != IntPtr.Zero;

        public static void Start()
        {
            if (_hook != IntPtr.Zero) return;

            _proc = HookProc;

            IntPtr hModule = IntPtr.Zero;
            try
            {
                using var proc = Process.GetCurrentProcess();
                using var mod = proc.MainModule!;
                hModule = GetModuleHandle(mod.ModuleName);
            }
            catch { /* ignore; hModule may remain zero for WH_MOUSE_LL */ }

            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, hModule, 0);

            // Refresh thresholds on start
            _dblTimeMs = GetDoubleClickTime();
            _dblCx = GetSystemMetrics(SM_CXDOUBLECLK);
            _dblCy = GetSystemMetrics(SM_CYDOUBLECLK);
        }

        public static void Stop()
        {
            if (_hook == IntPtr.Zero) return;
            try { UnhookWindowsHookEx(_hook); } catch { /* ignore */ }
            _hook = IntPtr.Zero;
            _proc = null;

            _guardTimer?.Stop();
            _guardTimer = null;
            _guardBusy = false;
        }

        private static IntPtr HookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            try
            {
                if (nCode >= 0 && wParam == (IntPtr)WM_LBUTTONDOWN)
                {
                    var ms = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                    long now = Environment.TickCount64;
                    bool withinTime = (now - _lastDownTick) <= _dblTimeMs;
                    bool withinX = Math.Abs(ms.pt.X - _lastDownPt.X) <= _dblCx;
                    bool withinY = Math.Abs(ms.pt.Y - _lastDownPt.Y) <= _dblCy;
                    bool isDouble = withinTime && withinX && withinY;

                    _lastDownTick = now;
                    _lastDownPt = ms.pt;

                    if (isDouble)
                    {
                        var disp = System.Windows.Application.Current?.Dispatcher;
                        if (disp != null)
                        {
                            disp.BeginInvoke(DispatcherPriority.Background, new Action(() =>
                            {
                                try
                                {
                                    // Only act on empty desktop to avoid noise
                                    if (!DesktopHelper.CursorIsOverEmptyDesktop())
                                        return;

                                    if (_guardBusy) return;
                                    _guardBusy = true;

                                    // Defer a hair so we don't collide with Explorer's double-click handling
                                    var delay = new DispatcherTimer { Interval = DelayBeforeToggle };
                                    delay.Tick += (s, e) =>
                                    {
                                        delay.Stop();
                                        try
                                        {
                                            DesktopHelper.ToggleDesktopIconsRobust();
                                        }
                                        catch { /* ignore */ }

                                        // Release guard after short interval
                                        _guardTimer?.Stop();
                                        _guardTimer = new DispatcherTimer { Interval = GuardInterval };
                                        _guardTimer.Tick += (s2, e2) =>
                                        {
                                            _guardTimer!.Stop();
                                            _guardBusy = false;
                                        };
                                        _guardTimer.Start();
                                    };
                                    delay.Start();
                                }
                                catch { /* ignore */ }
                            }));
                        }
                    }
                }
            }
            catch { /* ignore any hook exceptions */ }

            return CallNextHookEx(IntPtr.Zero, nCode, wParam, lParam);
        }

        // ----- P/Invoke -----
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public int mouseData;
            public int flags;
            public int time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string? lpModuleName);

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        [DllImport("user32.dll")]
        private static extern int GetDoubleClickTime();
    }
}