using System;
using System.Collections.Generic;
using System.Linq;

namespace Pickets.Services
{
    /// <summary>
    /// Ctrl+Z for organizing: which fence (and tab) each item is in, item order, and fences
    /// deleted along the way. Before an organizing action the app records every fence's items;
    /// undoing puts them back. Positions, colors and other looks aren't part of it, and neither
    /// are files: something sent to the Recycle Bin is restored from there.
    /// </summary>
    internal sealed class UndoHistory
    {
        public const int Limit = 30;

        /// <summary>One fence's items as they were, and where it sat in the fence list.</summary>
        public sealed record FenceState(FenceModel Model, int Index, List<string> Items,
                                        List<FenceTab> Tabs, int ActiveTab, FenceSort Sort);

        public sealed record Entry(string Description, IReadOnlyList<FenceState> Fences);

        /// <summary>What an undo changed, so the app can update the fence windows.</summary>
        public sealed record Result(string Description, List<FenceModel> Added,
                                    List<FenceModel> Removed, List<FenceModel> Changed);

        private readonly List<Entry> _entries = new();

        public int Count => _entries.Count;

        /// <summary>What Ctrl+Z would undo next, or null when there's nothing.</summary>
        public string? NextDescription => _entries.Count > 0 ? _entries[^1].Description : null;

        public void Record(string description, IReadOnlyList<FenceModel> fences)
        {
            _entries.Add(Capture(description, fences));
            if (_entries.Count > Limit) _entries.RemoveAt(0);
        }

        /// <summary>Forget everything (the fences were replaced, e.g. by a profile switch).</summary>
        public void Clear() => _entries.Clear();

        /// <summary>Undo the latest action on <paramref name="fences"/>, or null if there's none.</summary>
        public Result? Undo(List<FenceModel> fences, Func<string, bool> exists)
        {
            if (_entries.Count == 0) return null;
            var entry = _entries[^1];
            _entries.RemoveAt(_entries.Count - 1);
            return Apply(entry, fences, exists);
        }

        public static Entry Capture(string description, IReadOnlyList<FenceModel> fences) =>
            new(description, fences.Select((m, i) => new FenceState(
                m, i, m.ItemPaths.ToList(),
                m.Tabs.Select(t => new FenceTab { Name = t.Name, ItemPaths = t.ItemPaths.ToList() }).ToList(),
                m.ActiveTab, m.Sort)).ToList());

        /// <summary>
        /// Puts the recorded items back. Fences deleted since come back where they were. A fence
        /// that's not in the record (made since) gives back items the record places elsewhere,
        /// and is removed if that leaves it empty, like the fence "Move to new fence" made; an
        /// empty fence made with New Fence stays. Items that no longer exist on disk are skipped.
        /// </summary>
        public static Result Apply(Entry entry, List<FenceModel> fences, Func<string, bool> exists)
        {
            var added = new List<FenceModel>();
            var removed = new List<FenceModel>();
            var changed = new List<FenceModel>();
            bool Keep(string p) => p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) || exists(p);

            foreach (var s in entry.Fences.OrderBy(s => s.Index))
            {
                if (!fences.Contains(s.Model))
                {
                    fences.Insert(Math.Min(s.Index, fences.Count), s.Model);
                    added.Add(s.Model);
                }
                s.Model.ItemPaths = s.Items.Where(Keep).ToList();
                s.Model.Tabs = s.Tabs.Select(t => new FenceTab { Name = t.Name, ItemPaths = t.ItemPaths.Where(Keep).ToList() }).ToList();
                s.Model.ActiveTab = s.ActiveTab;
                s.Model.Sort = s.Sort;
                FenceTabs.Normalize(s.Model);
                changed.Add(s.Model);
            }

            var recorded = new HashSet<FenceModel>(entry.Fences.Select(s => s.Model));
            var placed = new HashSet<string>(recorded.Where(m => !m.IsPortal).SelectMany(m => m.ItemPaths),
                                             StringComparer.OrdinalIgnoreCase);
            foreach (var m in fences.ToList())
            {
                if (recorded.Contains(m) || m.IsPortal) continue;
                if (m.ItemPaths.RemoveAll(placed.Contains) == 0) continue;
                FenceTabs.Normalize(m);
                if (m.ItemPaths.Count == 0)
                {
                    fences.Remove(m);
                    removed.Add(m);
                }
                else changed.Add(m);
            }

            return new Result(entry.Description, added, removed, changed);
        }
    }
}
