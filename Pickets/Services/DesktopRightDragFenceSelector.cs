// File: Services/DesktopRightDragFenceSelector.cs
using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;

namespace Pickets.Services
{
    /// <summary>
    /// Global right-button "rubber-band" selection on the empty desktop.
    /// Shows a context menu with "Create fence here", "Create folder portal here…" or "Cancel".
    /// Multi-monitor & DPI aware.
    /// </summary>
    public sealed class DesktopRightDragFenceSelector : IDisposable
    {
        // ---------- Public static API ----------
        private static DesktopRightDragFenceSelector? _instance;

        public static void Start(Action<Rect> onConfirm, Action<Rect> onPortal, Action<Rect> onMonitor)
        {
            // Ensure WPF Application exists
            if (System.Windows.Application.Current == null)
                _ = new System.Windows.Application();

            _instance ??= new DesktopRightDragFenceSelector(onConfirm, onPortal, onMonitor);
            _instance.Hook();
        }

        public static void Stop()
        {
            if (_instance is null) return;
            _instance.Unhook();
            _instance.Dispose();
            _instance = null;
        }

        // ---------- Instance ----------
        private readonly Action<Rect> _onConfirm;
        private readonly Action<Rect> _onPortal;
        private readonly Action<Rect> _onMonitor;
        private IntPtr _hook = IntPtr.Zero;
        private LowLevelMouseProc? _proc;

        // Drag state (screen pixels)
        private bool _rightDownOnDesktop;   // we've taken over the current right-press
        private bool _dragging;             // movement passed the drag threshold
        private POINT _ptStartPx;
        private POINT _ptLastPx;

        private const int DragThresholdPx = 6;

        // Tag for right-clicks we synthesize ourselves, so the hook lets them through.
        private const long InjectedMarker = 0x0F0E;

        // Overlay
        private ScreenOverlay? _overlay;

        private DesktopRightDragFenceSelector(Action<Rect> onConfirm, Action<Rect> onPortal, Action<Rect> onMonitor)
        {
            _onConfirm = onConfirm;
            _onPortal = onPortal;
            _onMonitor = onMonitor;
        }

        // ---------- Hook lifecycle ----------
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

        // ---------- Mouse hook ----------
        private IntPtr MouseHookProc(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                var msg = (MouseMessage)wParam;
                var data = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);

                // Let our own synthesized right-clicks flow straight to Explorer.
                if ((long)data.dwExtraInfo == InjectedMarker)
                    return CallNextHookEx(_hook, nCode, wParam, lParam);

                switch (msg)
                {
                    case MouseMessage.WM_RBUTTONDOWN:
                        // Cheap classification only — IsLikelyDesktopUnderCursor avoids the
                        // blocking LVM_HITTEST that must never run on a hook thread.
                        if (DesktopHelper.IsLikelyDesktopUnderCursor())
                        {
                            // Take over this entire right-press. We must swallow BOTH the down
                            // and the up; swallowing only the up leaves the button "stuck down".
                            _rightDownOnDesktop = true;
                            _dragging = false;
                            _ptStartPx = data.pt;
                            _ptLastPx = data.pt;
                            return (IntPtr)1;
                        }
                        _rightDownOnDesktop = false;
                        break;

                    case MouseMessage.WM_MOUSEMOVE:
                        if (_rightDownOnDesktop)
                        {
                            _ptLastPx = data.pt;
                            if (!_dragging &&
                                (Math.Abs(data.pt.X - _ptStartPx.X) >= DragThresholdPx ||
                                 Math.Abs(data.pt.Y - _ptStartPx.Y) >= DragThresholdPx))
                            {
                                _dragging = true;
                                ShowOverlay();
                            }
                            if (_dragging) UpdateOverlay();
                        }
                        break;

                    case MouseMessage.WM_RBUTTONUP:
                        if (_rightDownOnDesktop)
                        {
                            _rightDownOnDesktop = false;

                            if (_dragging)
                            {
                                _dragging = false;
                                var rectDip = ScreenToDipRect(RectFromPointsPx(_ptStartPx, data.pt));
                                bool valid = rectDip.Width >= 16 && rectDip.Height >= 16;
                                FinishSelectionAndAsk(rectDip, valid);
                            }
                            else
                            {
                                // Just a right-click, no drag: replay a clean right-click so
                                // Explorer shows its normal desktop menu.
                                SynthesizeRightClick();
                            }
                            return (IntPtr)1; // swallow the real up
                        }
                        break;
                }
            }
            return CallNextHookEx(_hook, nCode, wParam, lParam);
        }

        private void ShowOverlay()
        {
            var startPx = _ptStartPx;
            System.Windows.Application.Current!.Dispatcher.BeginInvoke(() =>
            {
                if (_overlay is not null) { try { _overlay.Close(); } catch { } _overlay = null; }
                _overlay = new ScreenOverlay(
                    new SolidColorBrush(System.Windows.Media.Color.FromArgb(200, 120, 180, 255)), 1.5,
                    new SolidColorBrush(System.Windows.Media.Color.FromArgb(55, 120, 180, 255)), 6, topmost: false);
                _overlay.Show();
                _overlay.UpdateRectPx(new Rect(startPx.X, startPx.Y, 0, 0));
            });
        }

        private void UpdateOverlay()
        {
            // BeginInvoke (non-blocking) so the hook thread never stalls on the UI thread.
            var r = RectFromPointsPx(_ptStartPx, _ptLastPx);
            var rectPx = new Rect(r.X, r.Y, r.Width, r.Height);
            System.Windows.Application.Current!.Dispatcher.BeginInvoke(() =>
            {
                _overlay?.UpdateRectPx(rectPx);
            });
        }

        private static void SynthesizeRightClick()
        {
            // Cursor is already at the release point; tag these so our hook passes them through.
            mouse_event(MOUSEEVENTF_RIGHTDOWN, 0, 0, 0, (UIntPtr)InjectedMarker);
            mouse_event(MOUSEEVENTF_RIGHTUP, 0, 0, 0, (UIntPtr)InjectedMarker);
        }

        // ---------- Selection complete / menu ----------
        private void FinishSelectionAndAsk(Rect rectDip, bool showMenu)
        {
            System.Windows.Application.Current!.Dispatcher.BeginInvoke(
                new Action(() =>
                {
                    // Tear down overlay FIRST so it can't eat clicks
                    if (_overlay is not null)
                    {
                        _overlay.IsHitTestVisible = false;
                        try { _overlay.Hide(); } catch { }
                        try { _overlay.Close(); } catch { }
                        _overlay = null;
                    }

                    if (!showMenu) return;

                    // A tiny, activated owner window so the menu reliably receives the click —
                    // Explorer (not us) was the foreground app during the drag, and a bare
                    // ContextMenu popup with no active owner won't capture the mouse.
                    var owner = new Window
                    {
                        WindowStyle = WindowStyle.None,
                        ResizeMode = ResizeMode.NoResize,
                        AllowsTransparency = true,
                        Background = System.Windows.Media.Brushes.Transparent,
                        ShowInTaskbar = false,
                        Width = 1,
                        Height = 1,
                        Left = rectDip.Right,
                        Top = rectDip.Bottom,
                        Topmost = true
                    };
                    owner.Show();
                    owner.Activate();

                    var cm = new System.Windows.Controls.ContextMenu
                    {
                        Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
                        StaysOpen = false,
                        PlacementTarget = owner
                    };
                    if (System.Windows.Application.Current?.TryFindResource("DarkContextMenuStyle") is Style dark)
                        cm.Style = dark;

                    // Run the choice only after the menu and its owner window are gone: a dialog
                    // opened from the click (the portal's folder picker) would otherwise belong to
                    // the owner window and close with it.
                    Action<Rect>? chosen = null;
                    var miCreate = new System.Windows.Controls.MenuItem { Header = "Create fence here" };
                    miCreate.Click += (_, __) => chosen = _onConfirm;

                    var miPortal = new System.Windows.Controls.MenuItem { Header = "Create folder portal here…" };
                    miPortal.Click += (_, __) => chosen = _onPortal;

                    var miMonitor = new System.Windows.Controls.MenuItem { Header = "Create system monitor here" };
                    miMonitor.Click += (_, __) => chosen = _onMonitor;

                    var miCancel = new System.Windows.Controls.MenuItem { Header = "Cancel" };

                    cm.Items.Add(miCreate);
                    cm.Items.Add(miPortal);
                    cm.Items.Add(miMonitor);
                    cm.Items.Add(new System.Windows.Controls.Separator());
                    cm.Items.Add(miCancel);

                    cm.Closed += (_, __) =>
                    {
                        try { owner.Close(); } catch { }
                        if (chosen is { } action)
                            System.Windows.Application.Current!.Dispatcher.BeginInvoke(() => action(rectDip));
                    };
                    cm.IsOpen = true;
                }),
                System.Windows.Threading.DispatcherPriority.Background);
        }

        // ---------- Helpers ----------
        private static RectPx RectFromPointsPx(POINT a, POINT b)
        {
            int x1 = Math.Min(a.X, b.X);
            int y1 = Math.Min(a.Y, b.Y);
            int x2 = Math.Max(a.X, b.X);
            int y2 = Math.Max(a.Y, b.Y);
            return new RectPx(x1, y1, x2 - x1, y2 - y1);
        }

        // The new fence's rect in WPF units of the monitor where the drag started (see ScreenLayout).
        private static Rect ScreenToDipRect(RectPx rPx) =>
            ScreenLayout.DipFromPx(new Rect(rPx.X, rPx.Y, rPx.Width, rPx.Height));

        // ---------- P/Invoke ----------
        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);
        private const int WH_MOUSE_LL = 14;

        private enum MouseMessage
        {
            WM_MOUSEMOVE = 0x0200,
            WM_RBUTTONDOWN = 0x0204,
            WM_RBUTTONUP = 0x0205,
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

        private const uint MOUSEEVENTF_RIGHTDOWN = 0x0008;
        private const uint MOUSEEVENTF_RIGHTUP = 0x0010;

        [DllImport("user32.dll")]
        private static extern void mouse_event(uint dwFlags, uint dx, uint dy, uint dwData, UIntPtr dwExtraInfo);

        // Small int rect helper in pixels
        private readonly struct RectPx
        {
            public readonly int X, Y, Width, Height;
            public RectPx(int x, int y, int w, int h) { X = x; Y = y; Width = w; Height = h; }
        }
    }
}
