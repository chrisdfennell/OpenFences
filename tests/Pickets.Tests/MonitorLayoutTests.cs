using System.Collections.Generic;
using System.Linq;
using Pickets.Services;
using Xunit;

namespace Pickets.Tests
{
    public class MonitorLayoutTests
    {
        private static readonly string[] Drives = { "C:", "D:" };

        private static string[] Kinds(FenceModel m) => m.Tiles!.Select(t => t.Kind + t.Drive).ToArray();

        [Fact]
        public void A_monitor_from_1_13_showing_everything_gets_the_same_tiles()
        {
            var m = new FenceModel { IsMonitor = true };
            Assert.True(MonitorLayout.Normalize(m, Drives));
            Assert.Equal(new[] { "cpu", "memory", "diskC:", "diskD:", "network", "battery" }, Kinds(m));
            Assert.Null(m.Metrics);
        }

        [Fact]
        public void A_monitor_from_1_13_keeps_only_its_chosen_readings_in_the_usual_order()
        {
            var m = new FenceModel { IsMonitor = true, Metrics = new() { "network", "cpu" } };
            MonitorLayout.Normalize(m, Drives);
            Assert.Equal(new[] { "cpu", "network" }, Kinds(m));
            Assert.Null(m.Metrics);
        }

        [Fact]
        public void Tiles_and_other_fences_are_left_alone()
        {
            var tiles = new List<MonitorTileConfig> { new() { Kind = MonitorMetrics.Clock } };
            var monitor = new FenceModel { IsMonitor = true, Tiles = tiles };
            Assert.False(MonitorLayout.Normalize(monitor, Drives));
            Assert.Same(tiles, monitor.Tiles);

            var fence = new FenceModel();
            Assert.False(MonitorLayout.Normalize(fence, Drives));
            Assert.Null(fence.Tiles);
        }

        [Fact]
        public void New_monitors_start_with_cpu_memory_gpu_each_drive_network_and_battery()
        {
            Assert.Equal(new[] { "cpu", "memory", "gpu", "diskC:", "diskD:", "network", "battery" },
                         MonitorLayout.Defaults(Drives).Select(t => t.Kind + t.Drive));
        }

        [Fact]
        public void Tiles_are_named_by_the_user_or_by_what_they_show()
        {
            Assert.Equal("CPU", MonitorLayout.Label(new MonitorTileConfig { Kind = "cpu" }));
            Assert.Equal("Rig", MonitorLayout.Label(new MonitorTileConfig { Kind = "cpu", Label = "  Rig " }));
            var disk = new MonitorTileConfig { Kind = "disk", Drive = "D:" };
            Assert.Equal("Disk D:", MonitorLayout.Label(disk));
            Assert.Equal("Games (D:)", MonitorLayout.Label(disk, "Games"));
        }

        [Theory]
        [InlineData("cpu", 50, false, 0)]
        [InlineData("cpu", 80, false, 1)]
        [InlineData("gpu", 95, false, 2)]
        [InlineData("memory", 90, false, 2)]
        [InlineData("disk", 89, false, 0)]
        [InlineData("disk", 92, false, 1)]
        [InlineData("disk", 97, false, 2)]
        [InlineData("battery", 50, false, 0)]
        [InlineData("battery", 18, false, 1)]
        [InlineData("battery", 8, false, 2)]
        [InlineData("battery", 8, true, 0)]
        [InlineData("network", 100, false, 0)]
        public void Warning_levels(string kind, double value, bool charging, int expected)
        {
            Assert.Equal(expected, MonitorLayout.Level(kind, value, charging));
        }

        [Theory]
        [InlineData(2, 2, 60)]
        [InlineData(5, 1, 300)]
        [InlineData(1, 10, 6)]
        [InlineData(10, 1, 600)]
        public void Graphs_keep_the_graph_length_divided_by_the_interval(int minutes, int seconds, int expected)
        {
            Assert.Equal(expected, MonitorLayout.Capacity(minutes, seconds));
        }

        [Fact]
        public void Gpu_usage_is_the_busiest_engine_summed_over_apps()
        {
            var values = new[]
            {
                ("pid_100_luid_0x0_0xD1C2_phys_0_eng_0_engtype_3D", 20.0),
                ("pid_200_luid_0x0_0xD1C2_phys_0_eng_0_engtype_3D", 15.0),
                ("pid_100_luid_0x0_0xD1C2_phys_0_eng_3_engtype_VideoDecode", 30.0),
                ("pid_300_luid_0x0_0xD1C2_phys_0_eng_1_engtype_Copy", 2.0),
            };
            Assert.Equal(35, SystemMonitor.GpuPercent(values), 3);
            Assert.Equal(0, SystemMonitor.GpuPercent(System.Array.Empty<(string, double)>()));
            Assert.Equal(100, SystemMonitor.GpuPercent(new[] { ("pid_1_luid_a_eng_0", 70.0), ("pid_2_luid_a_eng_0", 60.0) }));
        }

        [Fact]
        public void Core_loads_come_in_core_order_without_the_total()
        {
            var values = new[] { ("10", 5.0), ("_Total", 50.0), ("2", 30.0), ("0", 120.0), ("1", 10.0) };
            Assert.Equal(new[] { 100.0, 10, 30, 5 }, SystemMonitor.CoreLoads(values));
        }

        [Theory]
        [InlineData(0, "0 bps")]
        [InlineData(125, "1 Kbps")]
        [InlineData(1_250_000, "10 Mbps")]
        [InlineData(187_500_000, "1.5 Gbps")]
        public void Speeds_can_be_shown_in_bits(double bytesPerSec, string expected)
        {
            Assert.Equal(expected, SystemMonitor.FormatRate(bytesPerSec, bits: true).Replace(',', '.'));
            Assert.EndsWith("/s", SystemMonitor.FormatRate(bytesPerSec));
        }

        [Fact]
        public void List_rows_show_the_value_unless_a_reading_sets_its_own_and_tooltips_carry_the_detail()
        {
            var cpu = new MonitorTile("cpu", "cpu", "CPU") { Value = "37%", Detail = "Up 2 h" };
            Assert.Equal("37%", cpu.Compact);
            Assert.StartsWith("Up 2 h", cpu.RowToolTip);

            var net = new MonitorTile("network", "network", "Network") { Value = "↓ 2 MB/s", Detail = "↑ 300 KB/s" };
            net.Compact = net.Value + "  " + net.Detail;
            net.Value = "↓ 3 MB/s"; // a later reading without a new compact line keeps the last one
            Assert.Equal("↓ 2 MB/s  ↑ 300 KB/s", net.Compact);
        }

        [Fact]
        public void A_tile_without_its_graph_shows_no_graph_bar_or_bars()
        {
            var cpu = new MonitorTile(new MonitorTileConfig { Kind = "cpu", Graph = false }, "cpu", "CPU");
            Assert.False(cpu.ShowsGraph || cpu.ShowsBar || cpu.ShowsBars);
            Assert.True(new MonitorTile("cores", "cores", "Cores").ShowsBars);
            Assert.True(new MonitorTile("battery", "battery", "Battery").ShowsBar);
            Assert.False(new MonitorTile("clock", "clock", "Clock").ShowsGraph);
        }
    }
}
