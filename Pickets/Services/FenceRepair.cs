using System;
using System.Collections.Generic;
using System.Linq;

namespace Pickets.Services
{
    /// <summary>
    /// Fixes configs left by older versions, where closing a fence with ✕ and later needing a
    /// fence of that name created duplicates (several "Desktop"/"Apps" fences).
    /// </summary>
    internal static class FenceRepair
    {
        /// <summary>Merges same-named real fences into the copy with the most items (keeping its
        /// position), and keeps each desktop item in only one fence: a specific fence wins over
        /// the catch-all. Portals are left alone. Returns true if anything changed.</summary>
        public static bool Repair(List<FenceModel> fences, string catchAllName)
        {
            bool changed = false;

            foreach (var group in fences.Where(f => !f.IsPortal)
                                        .GroupBy(f => f.Name, StringComparer.OrdinalIgnoreCase)
                                        .Where(g => g.Count() > 1)
                                        .ToList())
            {
                var keep = group.OrderByDescending(f => f.ItemPaths.Count).First();
                foreach (var dup in group.Where(f => !ReferenceEquals(f, keep)))
                {
                    foreach (var p in dup.ItemPaths)
                        if (!keep.ItemPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                            keep.ItemPaths.Add(p);
                    fences.Remove(dup);
                }
                changed = true;
            }

            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var order = fences.Where(f => !f.IsPortal)
                              .OrderBy(f => string.Equals(f.Name, catchAllName, StringComparison.OrdinalIgnoreCase) ? 1 : 0)
                              .ToList();
            foreach (var f in order)
            {
                int before = f.ItemPaths.Count;
                f.ItemPaths = f.ItemPaths.Where(p => claimed.Add(p)).ToList();
                if (f.ItemPaths.Count != before) changed = true;
            }

            return changed;
        }
    }
}
