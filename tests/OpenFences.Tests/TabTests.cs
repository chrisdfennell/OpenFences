using System.Collections.Generic;
using System.Linq;
using OpenFences;
using OpenFences.Services;
using Xunit;

namespace OpenFences.Tests
{
    public class TabTests
    {
        private static FenceModel Fence(params string[] items) => new() { Name = "Work", ItemPaths = items.ToList() };

        [Fact]
        public void Without_tabs_everything_is_on_tab_zero()
        {
            var m = Fence("a", "b");
            Assert.False(FenceTabs.HasTabs(m));
            Assert.Equal(0, FenceTabs.TabOf(m, "a"));
        }

        [Fact]
        public void Adding_a_tab_turns_tabs_on_and_keeps_existing_items_on_the_first()
        {
            var m = Fence("a", "b");
            int tab = FenceTabs.Add(m, "Games");

            Assert.Equal(1, tab);
            Assert.True(FenceTabs.HasTabs(m));
            Assert.Equal(new[] { "Main", "Games" }, m.Tabs.Select(t => t.Name));
            Assert.Equal(0, FenceTabs.TabOf(m, "a"));
        }

        [Fact]
        public void Moving_items_between_tabs()
        {
            var m = Fence("a", "b", "c");
            FenceTabs.Add(m, "Two");
            FenceTabs.Add(m, "Three");

            FenceTabs.MoveTo(m, new[] { "a", "b" }, 2);
            Assert.Equal(2, FenceTabs.TabOf(m, "a"));

            FenceTabs.MoveTo(m, new[] { "a" }, 1);
            Assert.Equal(1, FenceTabs.TabOf(m, "a"));
            Assert.Equal(2, FenceTabs.TabOf(m, "b"));

            FenceTabs.MoveTo(m, new[] { "b" }, 0);
            Assert.Equal(0, FenceTabs.TabOf(m, "b"));
        }

        [Fact]
        public void Deleting_a_tab_drops_its_items_back_to_the_first_and_turns_tabs_off_at_one()
        {
            var m = Fence("a", "b");
            FenceTabs.Add(m, "Two");
            FenceTabs.MoveTo(m, new[] { "a" }, 1);
            m.ActiveTab = 1;

            FenceTabs.Remove(m, 1);

            Assert.False(FenceTabs.HasTabs(m));
            Assert.Empty(m.Tabs);
            Assert.Equal(0, m.ActiveTab);
            Assert.Equal(new[] { "a", "b" }, m.ItemPaths); // nothing lost
        }

        [Fact]
        public void The_first_tab_cannot_be_deleted()
        {
            var m = Fence("a");
            FenceTabs.Add(m, "Two");
            FenceTabs.Remove(m, 0);
            Assert.Equal(2, m.Tabs.Count);
        }

        [Fact]
        public void Normalize_drops_items_the_fence_no_longer_owns_and_duplicates()
        {
            var m = Fence("a", "b");
            FenceTabs.Add(m, "Two");
            FenceTabs.Add(m, "Three");
            m.Tabs[1].ItemPaths.AddRange(new[] { "a", "gone" });
            m.Tabs[2].ItemPaths.Add("a"); // listed twice: first tab wins
            m.ActiveTab = 9;

            Assert.True(FenceTabs.Normalize(m));
            Assert.Equal(new[] { "a" }, m.Tabs[1].ItemPaths);
            Assert.Empty(m.Tabs[2].ItemPaths);
            Assert.Equal(2, m.ActiveTab);
        }

        [Fact]
        public void Portals_never_have_tabs()
        {
            var m = new FenceModel { IsPortal = true, Tabs = new List<FenceTab> { new(), new() } };
            FenceTabs.Normalize(m);
            Assert.Empty(m.Tabs);
        }
    }
}
