using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;

using WinForms = System.Windows.Forms;

namespace Pickets.Services
{
    /// <summary>
    /// Snaps a fence's edges while it's being moved or resized (WM_MOVING / WM_SIZING): to
    /// screen work-area edges, to other fences (aligned, or side by side with a small gap) and
    /// optionally to a grid. Everything here is in screen pixels. Hold Alt to move freely.
    /// </summary>
    internal static class FenceSnapper
    {
        public const int WM_SIZING = 0x0214;
        public const int WM_MOVING = 0x0216;

        // WM_SIZING wParam: which edge(s) the user is dragging
        private const int WMSZ_LEFT = 1, WMSZ_RIGHT = 2, WMSZ_TOP = 3, WMSZ_TOPLEFT = 4,
                          WMSZ_TOPRIGHT = 5, WMSZ_BOTTOM = 6, WMSZ_BOTTOMLEFT = 7, WMSZ_BOTTOMRIGHT = 8;

        private const double ThresholdDip = 10;
        private const double GapDip = 8;
        public const double GridDip = 20;

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }

        /// <summary>Adjusts the proposed rect in place. <paramref name="sizingEdge"/> is the
        /// WM_SIZING edge, or 0 for a move. Returns true if anything changed.</summary>
        public static bool Snap(ref RECT r, int sizingEdge, IEnumerable<IntPtr> otherFences, bool toEdges, bool toGrid)
        {
            if (!toEdges && !toGrid) return false;
            if ((GetKeyState(VK_MENU) & 0x8000) != 0) return false; // Alt = no snapping

            double scale = ScreenLayout.Scale;
            int threshold = (int)Math.Round(ThresholdDip * scale);
            int gap = (int)Math.Round(GapDip * scale);
            int grid = Math.Max(1, (int)Math.Round(GridDip * scale));

            var xs = new List<int>();
            var ys = new List<int>();
            if (toEdges)
            {
                foreach (var s in WinForms.Screen.AllScreens)
                {
                    var a = s.WorkingArea;
                    xs.Add(a.Left); xs.Add(a.Right);
                    ys.Add(a.Top); ys.Add(a.Bottom);
                }
                foreach (var h in otherFences)
                {
                    if (!GetWindowRect(h, out var o)) continue;
                    // Same edge (aligned) or the facing edge plus a gap (side by side).
                    xs.Add(o.Left); xs.Add(o.Right); xs.Add(o.Right + gap); xs.Add(o.Left - gap);
                    ys.Add(o.Top); ys.Add(o.Bottom); ys.Add(o.Bottom + gap); ys.Add(o.Top - gap);
                }
            }

            return SnapToLines(ref r, sizingEdge, xs, ys, threshold, toGrid ? grid : 0);
        }

        /// <summary>The snapping math, separate from the screen/window lookups so it can be
        /// tested: moves edges of <paramref name="r"/> onto the nearest vertical (xs) and
        /// horizontal (ys) lines within <paramref name="threshold"/>, or onto a grid of
        /// <paramref name="grid"/> pixels (0 = no grid). Returns true if anything changed.</summary>
        internal static bool SnapToLines(ref RECT r, int sizingEdge, List<int> xs, List<int> ys, int threshold, int grid)
        {
            bool toGrid = grid > 0;
            // Closest snap line within the threshold, or null if none.
            int? Find(int v, List<int> lines)
            {
                int? best = null; int bestD = threshold + 1;
                foreach (var c in lines)
                {
                    int d = Math.Abs(c - v);
                    if (d < bestD) { bestD = d; best = c; }
                }
                if (toGrid)
                {
                    int g = (int)Math.Round(v / (double)grid) * grid;
                    if (Math.Abs(g - v) < bestD && Math.Abs(g - v) <= threshold) best = g;
                }
                return best;
            }
            int Nearest(int v, List<int> lines) => Find(v, lines) ?? v;

            var before = r;
            if (sizingEdge == 0)
            {
                // Move: shift the whole rect by whichever edge is closest to a line.
                int dx = SmallestShift(r.Left, r.Right, xs, Find);
                int dy = SmallestShift(r.Top, r.Bottom, ys, Find);
                r.Left += dx; r.Right += dx;
                r.Top += dy; r.Bottom += dy;
            }
            else
            {
                // Resize: only the edges being dragged move.
                if (sizingEdge is WMSZ_LEFT or WMSZ_TOPLEFT or WMSZ_BOTTOMLEFT) r.Left = Nearest(r.Left, xs);
                if (sizingEdge is WMSZ_RIGHT or WMSZ_TOPRIGHT or WMSZ_BOTTOMRIGHT) r.Right = Nearest(r.Right, xs);
                if (sizingEdge is WMSZ_TOP or WMSZ_TOPLEFT or WMSZ_TOPRIGHT) r.Top = Nearest(r.Top, ys);
                if (sizingEdge is WMSZ_BOTTOM or WMSZ_BOTTOMLEFT or WMSZ_BOTTOMRIGHT) r.Bottom = Nearest(r.Bottom, ys);
            }
            return !before.Equals(r);
        }

        private static int SmallestShift(int lo, int hi, List<int> lines, Func<int, List<int>, int?> find)
        {
            int? dLo = find(lo, lines) - lo;
            int? dHi = find(hi, lines) - hi;
            if (dLo == null) return dHi ?? 0;
            if (dHi == null) return dLo.Value;
            return Math.Abs(dLo.Value) <= Math.Abs(dHi.Value) ? dLo.Value : dHi.Value;
        }

        private const int VK_MENU = 0x12;

        [DllImport("user32.dll")]
        private static extern short GetKeyState(int nVirtKey);

        [DllImport("user32.dll")]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    }
}
