using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Rectangle = System.Windows.Shapes.Rectangle;

namespace OpenFences.Services
{
    /// <summary>
    /// Passive global left-drag "lasso" on the empty desktop. It NEVER swallows mouse
    /// input (so normal clicking is untouched); it only draws a selection rectangle and
    /// reports the screen rect so the host can select fence items inside it. Only active
    /// while desktop icons are hidden (otherwise Explorer draws its own marquee).
    /// </summary>
    public sealed class DesktopLeftDragLasso : IDisposable
    {
        private static DesktopLeftDragLasso? _instance;

        // onUpdate(screenRectPx, additive) fires during the drag; onEnd fires on release.
        public static void Start(Action<Rect, bool> onUpdate, Action onEnd)
        {
            if (System.Windows.Application.Current == null) _ = new System.Windows.Application();
            _instance ??= new DesktopLeftDragLasso(onUpdate, onEnd);
            _instance.Hook();
        }

        public static void Stop()
        {
            if (_instance is null) return;
            _instance.Unhook();
            _instance.Dispose();
            _instance = null;
        }

        private readonly Action<Rect, bool> _onUpdate;
        private readonly Action _onEnd;
        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProc? _proc;

        private bool _armed;     // left button went down over empty desktop
        private bool _dragging;  // moved past threshold → actually lassoing
        private bool _additive;
        private POINT _startPx, _lastPx;

        private LassoOverlay? _overlay;
        private readonly double _dpiScaleX, _dpiScaleY;
        private const int ThresholdPx = 4;

        private DesktopLeftDragLasso(Action<Rect, bool> onUpdate, Action onEnd)
        {
            _onUpdate = onUpdate;
            _onEnd = onEnd;

            var visual = System.Windows.Application.Current?.MainWindow as Visual;
            var dpi = (visual != null) ? VisualTreeHelper.GetDpi(visual) : new DpiScale(1.0, 1.0);
            _dpiScaleX = dpi.DpiScaleX;
            _dpiScaleY = dpi.DpiScaleY;
        }

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

        public void Dispose()
        {
            try { _overlay?.Close(); } catch { }
            _overlay = null;
        }

        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = (MouseMessage)wParam;
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                switch (msg)
                {
                    case MouseMessage.WM_LBUTTONDOWN:
                        // Only lasso on the bare desktop while icons are hidden.
                        if (!DesktopHelper.AreIconsVisible() && DesktopHelper.IsLikelyDesktopUnderCursor())
                        {
                            _armed = true;
                            _dragging = false;
                            _startPx = _lastPx = data.pt;
                            _additive = (GetKeyState(VK_CONTROL) & 0x8000) != 0;
                        }
                        else _armed = false;
                        break;

                    case MouseMessage.WM_MOUSEMOVE:
                        if (_armed)
                        {
                            _lastPx = data.pt;
                            if (!_dragging &&
                                (Math.Abs(_lastPx.X - _startPx.X) > ThresholdPx ||
                                 Math.Abs(_lastPx.Y - _startPx.Y) > ThresholdPx))
                            {
                                _dragging = true;
                                ShowOverlay();
                            }
                            if (_dragging) UpdateDrag();
                        }
                        break;

                    case MouseMessage.WM_LBUTTONUP:
                        if (_armed)
                        {
                            _armed = false;
                            if (_dragging)
                            {
                                _dragging = false;
                                HideOverlay();
                                var endFn = _onEnd;
                                System.Windows.Application.Current?.Dispatcher.BeginInvoke(endFn);
                            }
                        }
                        break;
                }
            }
            // ALWAYS passive — never swallow left input.
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private Rect RectPx() => new Rect(
            Math.Min(_startPx.X, _lastPx.X), Math.Min(_startPx.Y, _lastPx.Y),
            Math.Abs(_lastPx.X - _startPx.X), Math.Abs(_lastPx.Y - _startPx.Y));

        private void ShowOverlay()
        {
            System.Windows.Application.Current!.Dispatcher.BeginInvoke(() =>
            {
                if (_overlay is not null) { try { _overlay.Close(); } catch { } _overlay = null; }
                _overlay = new LassoOverlay(_dpiScaleX, _dpiScaleY);
                _overlay.Show();
            });
        }

        private void HideOverlay()
        {
            System.Windows.Application.Current!.Dispatcher.BeginInvoke(() =>
            {
                if (_overlay is not null) { try { _overlay.Close(); } catch { } _overlay = null; }
            });
        }

        private void UpdateDrag()
        {
            var rectPx = RectPx();
            bool additive = _additive;
            System.Windows.Application.Current!.Dispatcher.BeginInvoke(() =>
            {
                _overlay?.UpdateRectPx(rectPx);
                _onUpdate(rectPx, additive);
            });
        }

        // ---------- Overlay ----------
        private sealed class LassoOverlay : Window
        {
            private readonly Canvas _canvas = new();
            private readonly Rectangle _rect = new();
            private readonly double _dpiX, _dpiY;

            public LassoOverlay(double dpiScaleX, double dpiScaleY)
            {
                _dpiX = dpiScaleX; _dpiY = dpiScaleY;

                WindowStyle = WindowStyle.None;
                ResizeMode = ResizeMode.NoResize;
                AllowsTransparency = true;
                Background = System.Windows.Media.Brushes.Transparent;
                ShowInTaskbar = false;
                Topmost = true;
                IsHitTestVisible = false;

                Width = SystemParameters.VirtualScreenWidth / dpiScaleX;
                Height = SystemParameters.VirtualScreenHeight / dpiScaleY;
                Left = SystemParameters.VirtualScreenLeft / dpiScaleX;
                Top = SystemParameters.VirtualScreenTop / dpiScaleY;

                _rect.Stroke = new SolidColorBrush(System.Windows.Media.Color.FromArgb(220, 127, 168, 224));
                _rect.StrokeThickness = 1;
                _rect.Fill = new SolidColorBrush(System.Windows.Media.Color.FromArgb(60, 90, 143, 216));
                _canvas.Children.Add(_rect);
                Content = _canvas;
            }

            public void UpdateRectPx(Rect rPx)
            {
                // Convert physical px → this overlay's DIP-local coordinates.
                double x = rPx.X / _dpiX - Left;
                double y = rPx.Y / _dpiY - Top;
                Canvas.SetLeft(_rect, x);
                Canvas.SetTop(_rect, y);
                _rect.Width = rPx.Width / _dpiX;
                _rect.Height = rPx.Height / _dpiY;
            }
        }

        // ---------- P/Invoke ----------
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_MOUSE_LL = 14;
        private const int VK_CONTROL = 0x11;

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

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);
    }
}
