using System.Collections.Generic;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class DesktopTakeoverTests
    {
        private static FenceModel Fence(params string[] items) => new() { Name = "F", ItemPaths = new List<string>(items) };

        [Fact]
        public void A_first_start_has_not_taken_over()
        {
            Assert.False(DesktopTakeover.IsTakenOver(null, new List<FenceModel>()));
        }

        [Fact]
        public void An_older_config_has_taken_over_once_a_fence_holds_desktop_items()
        {
            Assert.True(DesktopTakeover.IsTakenOver(null, new[] { Fence(@"C:\Users\a\Desktop\x.txt") }));
            Assert.False(DesktopTakeover.IsTakenOver(null, new[] { Fence() })); // only an empty fence
        }

        [Fact]
        public void A_saved_answer_wins()
        {
            Assert.False(DesktopTakeover.IsTakenOver(false, new[] { Fence(@"C:\x.txt") }));
            Assert.True(DesktopTakeover.IsTakenOver(true, new List<FenceModel>()));
        }

        [Fact]
        public void Portals_and_system_monitors_dont_count_as_holding_desktop_items()
        {
            var portal = new FenceModel { Name = "P", IsPortal = true, FolderPath = @"C:\Docs", ItemPaths = new List<string> { @"C:\Docs\a.txt" } };
            var monitor = new FenceModel { Name = "M", IsMonitor = true };
            Assert.False(DesktopTakeover.HoldsDesktopItems(new[] { portal, monitor }));
        }
    }
}
