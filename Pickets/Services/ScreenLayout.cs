using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using Point = System.Windows.Point;

namespace Pickets.Services
{
    /// <summary>
    /// Monitor-arrangement helpers. Fences remember a position per arrangement (docked vs.
    /// undocked laptop, projector attached…) and are kept on a visible screen when it changes.
    ///
    /// The app is per-monitor DPI aware (app.manifest), so each monitor has its own scale and
    /// WPF's Window.Left/Top on a monitor are its screen pixels divided by that monitor's scale.
    /// "DIP rects" below use that convention: a rect is converted with the scale of the monitor
    /// it's on. Screen pixels (hooks, GetWindowRect, WinForms.Screen) are physical.
    /// </summary>
    internal static class ScreenLayout
    {
        private static double? _scale;

        /// <summary>Screen pixels per WPF unit on the primary monitor (system DPI / 96). For
        /// anything tied to a place on screen, use ScaleAtPx or the conversions below.</summary>
        public static double Scale
        {
            get
            {
                if (_scale == null)
                {
                    try
                    {
                        using var g = Drawing.Graphics.FromHwnd(IntPtr.Zero);
                        _scale = g.DpiX / 96.0;
                    }
                    catch { _scale = 1.0; }
                }
                return _scale.Value;
            }
        }

        /// <summary>Identifies the current monitor arrangement, e.g. "0,0,1920,1080|1920,0,2560,1440".</summary>
        public static string CurrentKey() =>
            string.Join("|", WinForms.Screen.AllScreens
                .Select(s => s.Bounds)
                .OrderBy(b => b.X).ThenBy(b => b.Y)
                .Select(b => $"{b.X},{b.Y},{b.Width},{b.Height}"));

        /// <summary>Work areas (screen minus taskbar) of every monitor, as DIP rects.</summary>
        public static List<Rect> WorkAreas() =>
            WinForms.Screen.AllScreens.Select(sc => ToDip(sc.WorkingArea, ScaleOf(sc))).ToList();

        /// <summary>The work area (DIP rect) of the monitor under the mouse.</summary>
        public static Rect WorkAreaAtCursor()
        {
            var sc = WinForms.Screen.FromPoint(WinForms.Cursor.Position);
            return ToDip(sc.WorkingArea, ScaleOf(sc));
        }

        /// <summary>The mouse position in DIPs (of the monitor it's on).</summary>
        public static Point CursorDip()
        {
            var p = WinForms.Cursor.Position;
            double s = ScaleAtPx(p.X, p.Y);
            return new Point(p.X / s, p.Y / s);
        }

        /// <summary>Scale (DPI / 96) of the monitor at (or nearest to) a screen-pixel point.</summary>
        public static double ScaleAtPx(int x, int y)
        {
            try
            {
                var mon = MonitorFromPoint(new POINT { X = x, Y = y }, MONITOR_DEFAULTTONEAREST);
                if (GetDpiForMonitor(mon, 0, out uint dpi, out _) == 0 && dpi > 0) return dpi / 96.0;
            }
            catch { /* older Windows */ }
            return Scale;
        }

        private static double ScaleOf(WinForms.Screen sc)
        {
            var b = sc.Bounds;
            return ScaleAtPx(b.X + b.Width / 2, b.Y + b.Height / 2);
        }

        private static Rect ToDip(Drawing.Rectangle px, double s) => new(px.X / s, px.Y / s, px.Width / s, px.Height / s);

        /// <summary>Screen-pixel rect → DIP rect, using the scale of the monitor its top-left is on.</summary>
        public static Rect DipFromPx(Rect px)
        {
            double s = ScaleAtPx((int)px.X, (int)px.Y);
            return new Rect(px.X / s, px.Y / s, px.Width / s, px.Height / s);
        }

        /// <summary>DIP rect → screen pixels, using the monitor whose DIP bounds contain its
        /// top-left (or the nearest one: monitors at different scales leave gaps in DIP space).</summary>
        public static Rect PxFromDip(Rect dip)
        {
            var screens = WinForms.Screen.AllScreens.Select(sc => (dip: ToDip(sc.Bounds, ScaleOf(sc)), s: ScaleOf(sc))).ToList();
            if (screens.Count == 0) return dip;
            var p = dip.TopLeft;
            double s = screens.Where(x => x.dip.Contains(p)).Select(x => x.s).FirstOrDefault();
            if (s == 0) s = screens.OrderBy(x => DistanceSquared(x.dip, p)).First().s;
            return new Rect(dip.X * s, dip.Y * s, dip.Width * s, dip.Height * s);
        }

        private const uint MONITOR_DEFAULTTONEAREST = 2;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct POINT { public int X; public int Y; }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);

        [System.Runtime.InteropServices.DllImport("shcore.dll")]
        private static extern int GetDpiForMonitor(IntPtr hmonitor, int dpiType, out uint dpiX, out uint dpiY);

        /// <summary>
        /// Returns <paramref name="r"/> unchanged if enough of its title bar is on a screen to
        /// grab it; otherwise moves (and if needed shrinks) it onto the nearest work area.
        /// </summary>
        public static Rect FitOnScreen(Rect r) => FitOnScreen(r, WorkAreas());

        /// <summary>Same, against explicit work areas (WPF units); used by tests.</summary>
        internal static Rect FitOnScreen(Rect r, IReadOnlyList<Rect> areas)
        {
            if (areas.Count == 0) return r;

            // The title bar is what you drag, so that's what has to be reachable.
            var titleBar = new Rect(r.Left, r.Top, r.Width, 34);
            foreach (var a in areas)
            {
                var hit = Rect.Intersect(a, titleBar);
                if (!hit.IsEmpty && hit.Width >= Math.Min(80, r.Width) && hit.Height >= 20) return r;
            }

            var center = new Point(r.Left + r.Width / 2, r.Top + r.Height / 2);
            var target = areas.OrderBy(a => DistanceSquared(a, center)).First();

            double w = Math.Min(r.Width, target.Width);
            double h = Math.Min(r.Height, target.Height);
            double left = Math.Clamp(r.Left, target.Left, target.Right - w);
            double top = Math.Clamp(r.Top, target.Top, target.Bottom - h);
            return new Rect(left, top, w, h);
        }

        private static double DistanceSquared(Rect a, Point p)
        {
            double dx = Math.Max(Math.Max(a.Left - p.X, 0), p.X - a.Right);
            double dy = Math.Max(Math.Max(a.Top - p.Y, 0), p.Y - a.Bottom);
            return dx * dx + dy * dy;
        }
    }
}
