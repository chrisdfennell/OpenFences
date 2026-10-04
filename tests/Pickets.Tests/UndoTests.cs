using System.Collections.Generic;
using System.Linq;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class UndoTests
    {
        private static bool Exists(string _) => true;

        private static FenceModel Fence(string name, params string[] items) =>
            new() { Name = name, ItemPaths = items.ToList() };

        [Fact]
        public void Moving_items_between_fences_is_undone()
        {
            var desktop = Fence("Desktop", "a", "b");
            var apps = Fence("Apps", "c");
            var fences = new List<FenceModel> { desktop, apps };
            var history = new UndoHistory();

            history.Record("Move 1 item to “Apps”", fences);
            desktop.ItemPaths.Remove("a");
            apps.ItemPaths.Add("a");

            var result = history.Undo(fences, Exists)!;

            Assert.Equal("Move 1 item to “Apps”", result.Description);
            Assert.Equal(new[] { "a", "b" }, desktop.ItemPaths);
            Assert.Equal(new[] { "c" }, apps.ItemPaths);
            Assert.Null(history.Undo(fences, Exists));
        }

        [Fact]
        public void A_deleted_fence_comes_back_in_its_place_with_its_items()
        {
            var desktop = Fence("Desktop", "a");
            var work = Fence("Work", "b", "c");
            var games = Fence("Games", "d");
            var fences = new List<FenceModel> { desktop, work, games };
            var history = new UndoHistory();

            history.Record("Delete fence “Work”", fences);
            fences.Remove(work);
            desktop.ItemPaths.AddRange(new[] { "b", "c" });

            var result = history.Undo(fences, Exists)!;

            Assert.Equal(new[] { desktop, work, games }, fences);
            Assert.Equal(new[] { work }, result.Added);
            Assert.Equal(new[] { "a" }, desktop.ItemPaths);
            Assert.Equal(new[] { "b", "c" }, work.ItemPaths);
        }

        [Fact]
        public void A_fence_made_by_the_undone_action_goes_away_but_other_new_fences_stay()
        {
            var desktop = Fence("Desktop", "a", "b");
            var fences = new List<FenceModel> { desktop };
            var history = new UndoHistory();

            history.Record("Move 1 item to a new fence", fences);
            desktop.ItemPaths.Remove("b");
            var made = Fence("Fence 1", "b");
            fences.Add(made);
            var emptyLater = Fence("Fence 2");          // New Fence, not part of the action
            var portal = new FenceModel { Name = "Downloads", IsPortal = true };
            fences.Add(emptyLater);
            fences.Add(portal);

            var result = history.Undo(fences, Exists)!;

            Assert.Equal(new[] { made }, result.Removed);
            Assert.Equal(new[] { desktop, emptyLater, portal }, fences);
            Assert.Equal(new[] { "a", "b" }, desktop.ItemPaths);
        }

        [Fact]
        public void Tabs_order_and_sort_come_back_and_missing_files_are_skipped()
        {
            var f = Fence("Work", "a", "b", "c");
            f.Tabs = new List<FenceTab> { new() { Name = "Main" }, new() { Name = "Docs", ItemPaths = { "c" } } };
            f.ActiveTab = 1;
            var fences = new List<FenceModel> { f };
            var history = new UndoHistory();

            history.Record("Delete tab “Docs”", fences);
            FenceTabs.Remove(f, 1);
            f.ItemPaths = new List<string> { "c", "b", "a" };
            f.Sort = FenceSort.Manual;

            history.Undo(fences, p => p != "b");

            Assert.Equal(new[] { "a", "c" }, f.ItemPaths);
            Assert.Equal(new[] { "Main", "Docs" }, f.Tabs.Select(t => t.Name));
            Assert.Equal(new[] { "c" }, f.Tabs[1].ItemPaths);
            Assert.Equal(1, f.ActiveTab);
            Assert.Equal(FenceSort.Name, f.Sort);
        }

        [Fact]
        public void Only_the_latest_steps_are_kept()
        {
            var fences = new List<FenceModel> { Fence("Desktop") };
            var history = new UndoHistory();
            for (int i = 0; i < UndoHistory.Limit + 5; i++) history.Record($"Step {i}", fences);

            Assert.Equal(UndoHistory.Limit, history.Count);
            Assert.Equal($"Step {UndoHistory.Limit + 4}", history.NextDescription);
        }
    }
}
