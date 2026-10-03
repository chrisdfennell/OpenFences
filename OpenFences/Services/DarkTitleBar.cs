using System;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace OpenFences.Services
{
    /// <summary>Gives a standard-framed dialog a dark Windows title bar to match its content.</summary>
    internal static class DarkTitleBar
    {
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        public static void Apply(Window window)
        {
            window.SourceInitialized += (_, __) =>
            {
                try
                {
                    var hwnd = new WindowInteropHelper(window).Handle;
                    int on = 1;
                    DwmSetWindowAttribute(hwnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref on, sizeof(int));
                }
                catch { /* older Windows: keep the default frame */ }
            };
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);
    }
}
