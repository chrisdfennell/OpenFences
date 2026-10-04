using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;

using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using Point = System.Windows.Point;

namespace OpenFences.Services
{
    /// <summary>
    /// Monitor-arrangement helpers. Fences remember a position per arrangement (docked vs.
    /// undocked laptop, projector attached…) and are kept on a visible screen when it changes.
    /// The app is system-DPI aware, so screen pixels divide by one system scale to get the
    /// WPF units that Window.Left/Top use.
    /// </summary>
    internal static class ScreenLayout
    {
        private static double? _scale;

        /// <summary>Screen pixels per WPF unit (system DPI / 96).</summary>
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

        /// <summary>Work areas (screen minus taskbar) of every monitor, in WPF units.</summary>
        public static List<Rect> WorkAreas()
        {
            double s = Scale;
            return WinForms.Screen.AllScreens
                .Select(sc => sc.WorkingArea)
                .Select(a => new Rect(a.X / s, a.Y / s, a.Width / s, a.Height / s))
                .ToList();
        }

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
