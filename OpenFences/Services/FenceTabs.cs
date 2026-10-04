using System;
using System.Collections.Generic;
using System.Linq;

namespace OpenFences.Services
{
    /// <summary>
    /// Tabs inside a fence. A fence still owns all its items in FenceModel.ItemPaths; tabs only
    /// say which tab each item shows on. Tab 0 holds every item not listed in a later tab, so
    /// items never get lost: deleting a tab drops its items back to the first one.
    /// </summary>
    internal static class FenceTabs
    {
        public static bool HasTabs(FenceModel m) => !m.IsPortal && m.Tabs.Count >= 2;

        /// <summary>Which tab an item is on (0 when tabs are off).</summary>
        public static int TabOf(FenceModel m, string path)
        {
            if (!HasTabs(m)) return 0;
            for (int i = 1; i < m.Tabs.Count; i++)
                if (m.Tabs[i].ItemPaths.Contains(path, StringComparer.OrdinalIgnoreCase)) return i;
            return 0;
        }

        /// <summary>Put items on a tab (taking them off any other).</summary>
        public static void MoveTo(FenceModel m, IEnumerable<string> paths, int tab)
        {
            var list = paths.ToList();
            for (int i = 1; i < m.Tabs.Count; i++)
                m.Tabs[i].ItemPaths.RemoveAll(p => list.Contains(p, StringComparer.OrdinalIgnoreCase));
            if (tab > 0 && tab < m.Tabs.Count)
                foreach (var p in list)
                    m.Tabs[tab].ItemPaths.Add(p);
        }

        /// <summary>Adds a tab (turning tabs on with a first tab if needed); returns its index.</summary>
        public static int Add(FenceModel m, string name)
        {
            if (m.Tabs.Count == 0) m.Tabs.Add(new FenceTab { Name = "Main" });
            m.Tabs.Add(new FenceTab { Name = name });
            return m.Tabs.Count - 1;
        }

        /// <summary>Removes a tab; its items go back to the first tab. The first tab itself can't
        /// be removed. With one tab left, tabs switch off.</summary>
        public static void Remove(FenceModel m, int tab)
        {
            if (tab <= 0 || tab >= m.Tabs.Count) return;
            m.Tabs.RemoveAt(tab);
            if (m.ActiveTab >= tab) m.ActiveTab = Math.Max(0, m.ActiveTab - 1);
            Normalize(m);
        }

        /// <summary>Keeps tab data consistent with the fence's items: drops entries for items the
        /// fence no longer owns (or listed twice), switches tabs off below two, and keeps the
        /// active tab in range. Returns true if anything changed.</summary>
        public static bool Normalize(FenceModel m)
        {
            bool changed = false;
            if (m.IsPortal || m.Tabs.Count < 2)
            {
                if (m.Tabs.Count > 0 || m.ActiveTab != 0) changed = true;
                m.Tabs.Clear();
                m.ActiveTab = 0;
                return changed;
            }

            var owned = new HashSet<string>(m.ItemPaths, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            m.Tabs[0].ItemPaths.Clear(); // tab 0 is implicit
            for (int i = 1; i < m.Tabs.Count; i++)
            {
                int before = m.Tabs[i].ItemPaths.Count;
                m.Tabs[i].ItemPaths = m.Tabs[i].ItemPaths.Where(p => owned.Contains(p) && seen.Add(p)).ToList();
                if (m.Tabs[i].ItemPaths.Count != before) changed = true;
            }

            int active = Math.Clamp(m.ActiveTab, 0, m.Tabs.Count - 1);
            if (active != m.ActiveTab) { m.ActiveTab = active; changed = true; }
            return changed;
        }
    }
}
