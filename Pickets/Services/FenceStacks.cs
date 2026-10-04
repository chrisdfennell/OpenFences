using System.Collections.Generic;
using System.Windows;

namespace Pickets.Services
{
    /// <summary>
    /// Stacks: fences snapped one under another (touching, or with the snap gap between them)
    /// form a column. Dragging a title bar moves the whole column, and a fence that rolls up or
    /// grows pulls the fences under it along, so the stack stays together. Everything here is in
    /// screen pixels; callers leave out hidden and locked fences, which also breaks a stack there.
    /// </summary>
    internal static class FenceStacks
    {
        /// <summary>True when <paramref name="lower"/> sits right under <paramref name="upper"/>:
        /// its top at the other's bottom (give or take the snap gap) and overlapping sideways.</summary>
        public static bool Touches(Rect upper, Rect lower, double gapPx)
        {
            double d = lower.Top - upper.Bottom;
            return d >= -2 && d <= gapPx + 2 && upper.Left < lower.Right && lower.Left < upper.Right;
        }

        /// <summary>Indexes of the fences stacked under <paramref name="self"/>, all the way down.</summary>
        public static List<int> Below(IReadOnlyList<Rect> others, Rect self, double gapPx) =>
            Walk(others, self, gapPx, down: true, up: false);

        /// <summary>Indexes of every fence in <paramref name="self"/>'s column, above and below.</summary>
        public static List<int> Column(IReadOnlyList<Rect> others, Rect self, double gapPx) =>
            Walk(others, self, gapPx, down: true, up: true);

        private static List<int> Walk(IReadOnlyList<Rect> others, Rect self, double gapPx, bool down, bool up)
        {
            var found = new List<int>();
            var seen = new bool[others.Count];
            var queue = new Queue<Rect>();
            queue.Enqueue(self);
            while (queue.Count > 0)
            {
                var cur = queue.Dequeue();
                for (int i = 0; i < others.Count; i++)
                {
                    if (seen[i]) continue;
                    if ((down && Touches(cur, others[i], gapPx)) || (up && Touches(others[i], cur, gapPx)))
                    {
                        seen[i] = true;
                        found.Add(i);
                        queue.Enqueue(others[i]);
                    }
                }
            }
            return found;
        }
    }
}
