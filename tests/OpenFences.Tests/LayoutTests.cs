using System.Collections.Generic;
using System.Linq;
using System.Windows;
using OpenFences;
using OpenFences.Services;
using Xunit;

namespace OpenFences.Tests
{
    public class FenceRepairTests
    {
        private static FenceModel Fence(string name, params string[] items) =>
            new() { Name = name, ItemPaths = items.ToList() };

        [Fact]
        public void Duplicates_merge_into_the_copy_with_the_most_items()
        {
            var big = Fence("Desktop", "a", "b", "c");
            var fences = new List<FenceModel> { Fence("Desktop", "d"), big, Fence("desktop", "a", "e") };

            Assert.True(FenceRepair.Repair(fences, "Desktop"));

            var only = Assert.Single(fences);
            Assert.Same(big, only); // keeps its position/settings
            Assert.Equal(new[] { "a", "b", "c", "d", "e" }, only.ItemPaths);
        }

        [Fact]
        public void An_item_in_two_fences_stays_in_the_specific_one()
        {
            var desktop = Fence("Desktop", "x", "y");
            var apps = Fence("Apps", "y");
            var fences = new List<FenceModel> { desktop, apps };

            Assert.True(FenceRepair.Repair(fences, "Desktop"));

            Assert.Equal(new[] { "x" }, desktop.ItemPaths);
            Assert.Equal(new[] { "y" }, apps.ItemPaths);
        }

        [Fact]
        public void Portals_are_left_alone()
        {
            var fences = new List<FenceModel>
            {
                new() { Name = "Projects", IsPortal = true, FolderPath = @"C:\A" },
                new() { Name = "Projects", IsPortal = true, FolderPath = @"C:\B" },
            };
            Assert.False(FenceRepair.Repair(fences, "Desktop"));
            Assert.Equal(2, fences.Count);
        }

        [Fact]
        public void A_clean_config_is_unchanged() =>
            Assert.False(FenceRepair.Repair(new List<FenceModel> { Fence("Apps", "a"), Fence("Desktop", "b") }, "Desktop"));
    }

    public class SnapTests
    {
        private static FenceSnapper.RECT R(int l, int t, int r, int b) => new() { Left = l, Top = t, Right = r, Bottom = b };

        [Fact]
        public void A_move_snaps_the_nearest_edge_onto_a_line()
        {
            var r = R(13, 100, 213, 300); // left edge 13px from the x=0 screen edge… too far
            Assert.False(FenceSnapper.SnapToLines(ref r, 0, new List<int> { 0 }, new List<int>(), 10, 0));

            r = R(7, 100, 207, 300); // …7px is within the 10px threshold
            Assert.True(FenceSnapper.SnapToLines(ref r, 0, new List<int> { 0 }, new List<int>(), 10, 0));
            Assert.Equal(R(0, 100, 200, 300), r); // whole rect shifted, size unchanged
        }

        [Fact]
        public void An_edge_already_on_a_line_does_not_jump_to_another()
        {
            // Left edge sits exactly on 0; the right edge is 4px from 504. Staying put wins.
            var r = R(0, 0, 500, 100);
            Assert.False(FenceSnapper.SnapToLines(ref r, 0, new List<int> { 0, 504 }, new List<int>(), 10, 0));
        }

        [Fact]
        public void Resizing_moves_only_the_dragged_edge()
        {
            const int WMSZ_RIGHT = 2;
            var r = R(100, 100, 395, 300);
            Assert.True(FenceSnapper.SnapToLines(ref r, WMSZ_RIGHT, new List<int> { 90, 400 }, new List<int>(), 10, 0));
            Assert.Equal(R(100, 100, 400, 300), r);
        }

        [Fact]
        public void Grid_snapping_rounds_to_the_grid()
        {
            var r = R(43, 18, 243, 218);
            Assert.True(FenceSnapper.SnapToLines(ref r, 0, new List<int>(), new List<int>(), 10, 20));
            Assert.Equal(R(40, 20, 240, 220), r);
        }
    }

    public class ScreenFitTests
    {
        private static readonly List<Rect> TwoMonitors = new() { new Rect(0, 0, 1920, 1040), new Rect(1920, 0, 1920, 1040) };

        [Fact]
        public void A_fence_on_screen_is_left_where_it_is()
        {
            var r = new Rect(2000, 100, 400, 300);
            Assert.Equal(r, ScreenLayout.FitOnScreen(r, TwoMonitors));
        }

        [Fact]
        public void A_fence_on_a_disconnected_monitor_moves_onto_the_nearest_one()
        {
            var fit = ScreenLayout.FitOnScreen(new Rect(4200, 300, 400, 300), TwoMonitors);
            Assert.Equal(new Rect(3440, 300, 400, 300), fit); // pulled onto the right-hand monitor
        }

        [Fact]
        public void A_fence_above_the_screen_comes_down_so_its_title_bar_is_reachable()
        {
            var fit = ScreenLayout.FitOnScreen(new Rect(100, -500, 400, 300), TwoMonitors);
            Assert.Equal(0, fit.Top);
        }

        [Fact]
        public void A_fence_bigger_than_the_screen_is_shrunk_to_fit()
        {
            var fit = ScreenLayout.FitOnScreen(new Rect(-5000, 0, 3000, 2000), TwoMonitors);
            Assert.True(fit.Width <= 1920 && fit.Height <= 1040);
        }
    }

    public class SnapshotTests
    {
        [Fact]
        public void Cloning_keeps_every_setting_and_shares_nothing()
        {
            var original = new List<FenceModel>
            {
                new()
                {
                    Name = "Apps", ItemPaths = { "a" }, AccentColor = "#3B82F6", Closed = true, Locked = true,
                    Glass = true, Sort = FenceSort.Manual, IconSize = FenceIconSize.Large,
                    BackgroundMedia = @"C:\Pictures\beach.jpg", BackgroundFit = FenceBackgroundFit.Fit, BackgroundDim = 0.2, BackgroundMuted = false,
                    Layouts = { ["0,0,1920,1080"] = new FenceRect { Left = 1, Top = 2, Width = 3, Height = 4 } }
                }
            };

            var copy = Assert.Single(LayoutSnapshots.Clone(original));
            Assert.NotSame(original[0], copy);
            Assert.Equal("#3B82F6", copy.AccentColor);
            Assert.True(copy.Closed && copy.Locked && copy.Glass);
            Assert.Equal(FenceSort.Manual, copy.Sort);
            Assert.Equal(FenceIconSize.Large, copy.IconSize);
            Assert.Equal(@"C:\Pictures\beach.jpg", copy.BackgroundMedia);
            Assert.Equal(FenceBackgroundFit.Fit, copy.BackgroundFit);
            Assert.Equal(0.2, copy.BackgroundDim);
            Assert.False(copy.BackgroundMuted);
            Assert.Equal(3, copy.Layouts["0,0,1920,1080"].Width);

            copy.ItemPaths.Add("b");
            Assert.Single(original[0].ItemPaths);
        }
    }
}
