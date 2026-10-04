using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace Pickets.Services
{
    /// <summary>Gives a standard-framed dialog a Windows title bar that matches the app's light or
    /// dark theme, and keeps it matching if the theme changes while the dialog is open.</summary>
    internal static class DarkTitleBar
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void Apply(Window window)
        {
            void Update()
            {
                try
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    if (hwnd == IntPtr.Zero) return;
                    int dark = Theme.IsLight ? 0 : 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref dark, sizeof(int));
                }
                catch { /* older Windows: keep the default frame */ }
            }

            window.SourceInitialized += (_, __) => Update();
            Theme.Changed += Update;
            window.Closed += (_, __) => Theme.Changed -= Update;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
