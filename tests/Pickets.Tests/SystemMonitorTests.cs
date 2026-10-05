using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class SystemMonitorTests
    {
        [Fact]
        public void Cpu_percent_is_the_busy_share_of_elapsed_time()
        {
            // Kernel time includes idle time: 100 elapsed (60 kernel + 40 user), 25 of it idle.
            var before = new SystemMonitor.CpuTimes(Idle: 1000, Kernel: 5000, User: 3000);
            var after = new SystemMonitor.CpuTimes(Idle: 1025, Kernel: 5060, User: 3040);
            Assert.Equal(75, SystemMonitor.CpuPercent(before, after), 3);
        }

        [Fact]
        public void Cpu_percent_is_zero_when_no_time_passed_or_counters_went_backwards()
        {
            var t = new SystemMonitor.CpuTimes(10, 20, 30);
            Assert.Equal(0, SystemMonitor.CpuPercent(t, t));
            Assert.Equal(0, SystemMonitor.CpuPercent(t, new SystemMonitor.CpuTimes(5, 40, 40)));
        }

        [Fact]
        public void Network_rates_skip_adapters_that_came_or_went_or_reset()
        {
            var before = new Dictionary<string, (long, long)>
            {
                ["wifi"] = (1_000, 500),
                ["vpn"] = (9_000_000, 9_000_000),  // reset below: counters went backwards
                ["gone"] = (5, 5),
            };
            var after = new Dictionary<string, (long, long)>
            {
                ["wifi"] = (5_000, 1_500),
                ["vpn"] = (10, 10),
                ["new"] = (7_000_000, 7_000_000),
            };
            var (down, up) = SystemMonitor.NetworkRates(before, after, seconds: 2);
            Assert.Equal(2_000, down);
            Assert.Equal(500, up);
            Assert.Equal((0.0, 0.0), SystemMonitor.NetworkRates(before, after, seconds: 0));
        }

        [Theory]
        [InlineData(0, "0 B")]
        [InlineData(512, "512 B")]
        [InlineData(1536, "1.5 KB")]
        [InlineData(15 * 1024, "15 KB")]
        [InlineData(250 * 1024 * 1024, "250 MB")]
        [InlineData(16L * 1024 * 1024 * 1024, "16 GB")]
        public void Bytes_are_shown_like_Explorer(long bytes, string expected)
        {
            Assert.Equal(expected, SystemMonitor.FormatBytes(bytes).Replace(',', '.'));
        }

        [Theory]
        [InlineData(30, "less than a minute")]
        [InlineData(45 * 60, "45 min")]
        [InlineData(2 * 3600, "2 h")]
        [InlineData(3 * 3600 + 12 * 60, "3 h 12 min")]
        public void Battery_time_reads_naturally(int seconds, string expected)
        {
            Assert.Equal(expected, SystemMonitor.FormatDuration(seconds));
        }

        [Fact]
        public void Percent_graphs_scale_to_a_fixed_top()
        {
            Assert.Equal(new[] { 0.0, 0.5, 1.0 }, MonitorTile.Scale(new[] { 0.0, 50, 150 }, 100));
        }

        [Fact]
        public void Speed_graphs_scale_to_the_peak_but_a_quiet_line_stays_low()
        {
            var busy = MonitorTile.Scale(new[] { 1_000_000.0, 2_000_000 }, null);
            Assert.Equal(new[] { 0.5, 1.0 }, busy);

            var quiet = MonitorTile.Scale(new[] { 100.0, 200 }, null);
            Assert.True(quiet.All(v => v < 0.01));
        }

        [Fact]
        public void A_tile_keeps_only_recent_history()
        {
            var tile = new MonitorTile(MonitorMetrics.Cpu, MonitorMetrics.Cpu, "CPU");
            for (int i = 0; i < MonitorTile.HistoryLength + 25; i++) tile.Push(i % 100, 100);
            Assert.Equal(MonitorTile.HistoryLength, tile.Graph.Count);
            Assert.True(tile.ShowsGraph);
            Assert.True(new MonitorTile(MonitorMetrics.Disk, "disk:C:", "C:").ShowsBar);
        }

        [Fact]
        public void Monitors_are_saved_with_their_readings_but_hold_no_desktop_items()
        {
            var m = new FenceModel { Name = "System", IsMonitor = true, Metrics = new() { "cpu", "network" } };
            Assert.False(m.HoldsDesktopItems);
            Assert.True(new FenceModel().HoldsDesktopItems);
            Assert.False(new FenceModel { IsPortal = true }.HoldsDesktopItems);

            var json = JsonSerializer.Serialize(m);
            Assert.DoesNotContain(nameof(FenceModel.HoldsDesktopItems), json);
            var back = JsonSerializer.Deserialize<FenceModel>(json)!;
            Assert.True(back.IsMonitor);
            Assert.Equal(new[] { "cpu", "network" }, back.Metrics);
        }

        [Fact]
        public void Repair_leaves_a_monitor_alone_even_when_it_shares_a_fence_name()
        {
            var fence = new FenceModel { Name = "System", ItemPaths = new() { @"C:\Users\me\Desktop\a.txt" } };
            var monitor = new FenceModel { Name = "System", IsMonitor = true };
            var fences = new List<FenceModel> { fence, monitor };

            Assert.False(FenceRepair.Repair(fences, "Desktop"));
            Assert.Equal(2, fences.Count);
            Assert.Empty(monitor.ItemPaths);
        }
    }
}
