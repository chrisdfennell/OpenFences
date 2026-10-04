using System;
using System.Runtime.InteropServices;

namespace Pickets.Services
{
    /// <summary>
    /// Frosted-glass fences: Windows blurs whatever is behind the window (the accent-policy
    /// blur that works with WPF's transparent windows), clipped to the fence's rounded shape.
    /// </summary>
    internal static class WindowBlur
    {
        public static void Set(IntPtr hwnd, bool enabled)
        {
            if (hwnd == IntPtr.Zero) return;
            var accent = new ACCENT_POLICY { AccentState = enabled ? ACCENT_ENABLE_BLURBEHIND : ACCENT_DISABLED };
            int size = Marshal.SizeOf(accent);
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                Marshal.StructureToPtr(accent, ptr, false);
                var data = new WINDOWCOMPOSITIONATTRIBDATA { Attribute = WCA_ACCENT_POLICY, Data = ptr, SizeOfData = size };
                SetWindowCompositionAttribute(hwnd, ref data);
            }
            catch { /* not supported on this Windows build */ }
            finally { Marshal.FreeHGlobal(ptr); }

            if (!enabled) SetWindowRgn(hwnd, IntPtr.Zero, true);
        }

        /// <summary>Clip the window (and so the blur) to a rounded rectangle, in pixels.</summary>
        public static void SetRoundedRegion(IntPtr hwnd, int width, int height, int radius)
        {
            if (hwnd == IntPtr.Zero || width <= 0 || height <= 0) return;
            var rgn = CreateRoundRectRgn(0, 0, width + 1, height + 1, radius * 2, radius * 2);
            if (SetWindowRgn(hwnd, rgn, true) == 0) DeleteObject(rgn); // on success the system owns it
        }

        private const int WCA_ACCENT_POLICY = 19;
        private const int ACCENT_DISABLED = 0, ACCENT_ENABLE_BLURBEHIND = 3;

        [StructLayout(LayoutKind.Sequential)]
        private struct ACCENT_POLICY
        {
            public int AccentState;
            public int AccentFlags;
            public uint GradientColor;
            public int AnimationId;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct WINDOWCOMPOSITIONATTRIBDATA
        {
            public int Attribute;
            public IntPtr Data;
            public int SizeOfData;
        }

        [DllImport("user32.dll")] private static extern int SetWindowCompositionAttribute(IntPtr hwnd, ref WINDOWCOMPOSITIONATTRIBDATA data);
        [DllImport("gdi32.dll")] private static extern IntPtr CreateRoundRectRgn(int x1, int y1, int x2, int y2, int w, int h);
        [DllImport("user32.dll")] private static extern int SetWindowRgn(IntPtr hWnd, IntPtr hRgn, bool redraw);
        [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr h);
    }
}
