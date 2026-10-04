using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Interop;

using WinForms = System.Windows.Forms;

namespace Pickets.Services
{
    /// <summary>
    /// "Fences in front" (Ctrl+Alt+Space): lifts every shown fence above app windows so you can
    /// use them without minimizing anything. It ends when another app (or the desktop, or the
    /// taskbar) becomes the foreground window, e.g. because you clicked it or opened an item,
    /// or on Esc or the shortcut again. While it's on, fences stay on top when clicked.
    /// </summary>
    internal static class FencesInFront
    {
        public static bool IsActive { get; private set; }

        private static IntPtr _hook;
        private static WinEventDelegate? _proc; // kept alive while the hook exists
        private static Action? _onExit;

        public static void Toggle(Action? onExit = null)
        {
            if (IsActive) Exit();
            else Enter(onExit);
        }

        public static void Enter(Action? onExit = null)
        {
            var shown = FenceWindow.AllFences.Where(f => f.IsVisible).ToList();
            if (IsActive || shown.Count == 0) { onExit?.Invoke(); return; }

            IsActive = true;
            _onExit = onExit;
            foreach (var f in shown) f.SetRaised(true);

            // Activate a fence (the one under the mouse if any) so keyboard input goes to the
            // fences and the next click on another app is a foreground change we can see.
            var cursor = WinForms.Cursor.Position;
            var target = shown.FirstOrDefault(f =>
                DesktopHelper.WindowRectPx(new WindowInteropHelper(f).Handle).Contains(cursor.X, cursor.Y)) ?? shown[0];
            target.Activate();

            _proc = OnForegroundChanged;
            _hook = SetWinEventHook(EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, IntPtr.Zero, _proc,
                                    0, 0, WINEVENT_OUTOFCONTEXT);
        }

        public static void Exit()
        {
            if (!IsActive) return;
            IsActive = false;

            if (_hook != IntPtr.Zero) UnhookWinEvent(_hook);
            _hook = IntPtr.Zero;
            _proc = null;

            foreach (var f in FenceWindow.AllFences.Where(f => f.IsVisible)) f.SetRaised(false);

            var done = _onExit;
            _onExit = null;
            done?.Invoke();
        }

        // Our own windows (fences, search, Quick Look, dialogs) keep the fences up; anything
        // else coming to the foreground means the user has moved on.
        private static void OnForegroundChanged(IntPtr hook, uint evt, IntPtr hwnd, int idObject, int idChild,
                                                uint thread, uint time)
        {
            if (hwnd == IntPtr.Zero) return;
            GetWindowThreadProcessId(hwnd, out uint pid);
            if (pid == (uint)Environment.ProcessId) return;
            Exit();
        }

        // ----- P/Invoke -----
        private const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
        private const uint WINEVENT_OUTOFCONTEXT = 0x0000;

        private delegate void WinEventDelegate(IntPtr hWinEventHook, uint eventType, IntPtr hwnd, int idObject,
                                               int idChild, uint dwEventThread, uint dwmsEventTime);

        [DllImport("user32.dll")]
        private static extern IntPtr SetWinEventHook(uint eventMin, uint eventMax, IntPtr hmodWinEventProc,
                                                     WinEventDelegate lpfnWinEventProc, uint idProcess,
                                                     uint idThread, uint dwFlags);

        [DllImport("user32.dll")]
        private static extern bool UnhookWinEvent(IntPtr hWinEventHook);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);
    }
}
