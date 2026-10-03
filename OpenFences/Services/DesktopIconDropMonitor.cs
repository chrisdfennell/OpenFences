using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Media;

namespace OpenFences.Services
{
    /// <summary>
    /// Passive global monitor that fires once after the user finishes a left-drag that
    /// started on the desktop — i.e. after they drag a real desktop icon and drop it.
    /// It NEVER swallows input (icon dragging stays native). The host uses the callback to
    /// reconcile fence ownership against the icons' new positions (drag-in / drag-out).
    /// </summary>
    public sealed class DesktopIconDropMonitor : IDisposable
    {
        private static DesktopIconDropMonitor? _instance;

        public static void Start(Action onDrop)
        {
            if (System.Windows.Application.Current == null) _ = new System.Windows.Application();
            _instance ??= new DesktopIconDropMonitor(onDrop);
            _instance.Hook();
        }

        public static void Stop()
        {
            if (_instance is null) return;
            _instance.Unhook();
            _instance.Dispose();
            _instance = null;
        }

        private readonly Action _onDrop;
        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProc? _proc;

        private bool _armed;     // left went down over the desktop
        private bool _dragging;  // moved past threshold
        private POINT _startPx;
        private const int ThresholdPx = 5;

        private DesktopIconDropMonitor(Action onDrop) => _onDrop = onDrop;

        private void Hook()
        {
            if (_hook != IntPtr.Zero) return;
            _proc = MouseHookProc;
            using var curProcess = Process.GetCurrentProcess();
            using var curModule = curProcess.MainModule!;
            _hook = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
        }

        private void Unhook()
        {
            if (_hook == IntPtr.Zero) return;
            UnhookWindowsHookEx(_hook);
            _hook = IntPtr.Zero;
        }

        public void Dispose() { }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = (MouseMessage)wParam;
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                switch (msg)
                {
                    case MouseMessage.WM_LBUTTONDOWN:
                        // Arm when the press lands on the desktop (on an icon or whitespace).
                        _armed = DesktopHelper.IsLikelyDesktopUnderCursor();
                        _dragging = false;
                        _startPx = data.pt;
                        break;

                    case MouseMessage.WM_MOUSEMOVE:
                        if (_armed && !_dragging &&
                            (Math.Abs(data.pt.X - _startPx.X) > ThresholdPx ||
                             Math.Abs(data.pt.Y - _startPx.Y) > ThresholdPx))
                            _dragging = true;
                        break;

                    case MouseMessage.WM_LBUTTONUP:
                        if (_armed && _dragging)
                        {
                            _armed = _dragging = false;
                            var fn = _onDrop;
                            // Let Explorer commit the icon's new position first, then reconcile.
                            System.Windows.Application.Current?.Dispatcher.BeginInvoke(fn,
                                System.Windows.Threading.DispatcherPriority.Background);
                        }
                        else _armed = _dragging = false;
                        break;
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam); // always passive
        }

        // ---------- P/Invoke ----------
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_MOUSE_LL = 14;

        private enum MouseMessage
        {
            WM_MOUSEMOVE = 0x0200,
            WM_LBUTTONDOWN = 0x0201,
            WM_LBUTTONUP = 0x0202,
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        private struct MSLLHOOKSTRUCT
        {
            public POINT pt;
            public uint mouseData;
            public uint flags;
            public uint time;
            public IntPtr dwExtraInfo;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);
    }
}
