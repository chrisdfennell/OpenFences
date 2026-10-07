using System.Collections.Generic;
using System.Linq;

namespace Pickets.Services
{
    /// <summary>
    /// Pickets leaves the desktop alone until desktop items are first put in a fence (Auto-Import,
    /// dragging items in, restoring a layout…). Only then does it hide the real desktop icons and
    /// show everything not in another fence in the Desktop fence. A fresh start changes nothing.
    /// </summary>
    internal static class DesktopTakeover
    {
        /// <summary>Whether Pickets has taken over: the saved answer, or for a config from before
        /// it was saved, whether any fence holds desktop items.</summary>
        public static bool IsTakenOver(bool? saved, IEnumerable<FenceModel> fences) =>
            saved ?? HoldsDesktopItems(fences);

        /// <summary>Whether any fence holds desktop items (portals and system monitors don't).</summary>
        public static bool HoldsDesktopItems(IEnumerable<FenceModel> fences) =>
            fences.Any(f => f.HoldsDesktopItems && f.ItemPaths.Count > 0);
    }
}
