using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace OpenFences.Services
{
    /// <summary>
    /// System-wide keyboard shortcuts (RegisterHotKey) delivered to the main window's HWND,
    /// which stays alive while it's hidden in the tray.
    /// </summary>
    internal sealed class GlobalHotkeys : IDisposable
    {
        public const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000;
        private const int WM_HOTKEY = 0x0312;

        private readonly HwndSource _source;
        private readonly Dictionary<int, Action> _actions = new();
        private int _nextId = 0xB000;

        public GlobalHotkeys(IntPtr hwnd)
        {
            _source = HwndSource.FromHwnd(hwnd) ?? throw new InvalidOperationException("No HwndSource for hotkeys.");
            _source.AddHook(WndProc);
        }

        /// <summary>Returns false if another app already owns this shortcut.</summary>
        public bool Register(uint modifiers, uint virtualKey, Action action)
        {
            int id = _nextId++;
            if (!RegisterHotKey(_source.Handle, id, modifiers | MOD_NOREPEAT, virtualKey)) return false;
            _actions[id] = action;
            return true;
        }

        public void UnregisterAll()
        {
            foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
            _actions.Clear();
        }

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
            {
                handled = true;
                action();
            }
            return IntPtr.Zero;
        }

        public void Dispose()
        {
            UnregisterAll();
            _source.RemoveHook(WndProc);
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);
    }
}
