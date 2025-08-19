using System;
using System.Runtime.InteropServices;

namespace OpenFences
{
    internal static class DesktopHelper
    {
        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindow(string lpClassName, string? lpWindowName);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr FindWindowEx(IntPtr hwndParent, IntPtr hwndChildAfter, string lpszClass, string? lpszWindow);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        static readonly IntPtr HWND_BOTTOM = new IntPtr(1);
        const uint SWP_NOMOVE = 0x0002;
        const uint SWP_NOSIZE = 0x0001;
        const uint SWP_NOACTIVATE = 0x0010;
        const int SW_HIDE = 0;
        const int SW_SHOW = 5;

        private static bool _iconsHidden = false;

        public static void ToggleDesktopIcons()
        {
            var list = GetDesktopListView();
            if (list == IntPtr.Zero) return;
            _iconsHidden = !_iconsHidden;
            ShowWindow(list, _iconsHidden ? SW_HIDE : SW_SHOW);
        }

        public static void SendToDesktopLayer(IntPtr hwnd)
        {
            SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0, SWP_NOACTIVATE | SWP_NOMOVE | SWP_NOSIZE);
        }

        private static IntPtr GetDesktopListView()
        {
            var prog = FindWindow("Progman", "Program Manager");
            var shellView = FindWindowEx(prog, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (shellView != IntPtr.Zero)
                return FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");

            IntPtr worker = IntPtr.Zero;
            while (true)
            {
                worker = FindWindowEx(IntPtr.Zero, worker, "WorkerW", null);
                if (worker == IntPtr.Zero) break;
                shellView = FindWindowEx(worker, IntPtr.Zero, "SHELLDLL_DefView", null);
                if (shellView != IntPtr.Zero)
                    return FindWindowEx(shellView, IntPtr.Zero, "SysListView32", "FolderView");
            }
            return IntPtr.Zero;
        }
    }
}