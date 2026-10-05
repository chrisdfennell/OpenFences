using System;
using System.Collections.Generic;
using System.Linq;

namespace Pickets.Services
{
    /// <summary>
    /// The tiles on a system monitor fence: the defaults for a new monitor, converting a Pickets
    /// 1.13 monitor (a list of readings) into tiles, names, and the warning levels behind the
    /// amber and red colors.
    /// </summary>
    public static class MonitorLayout
    {
        /// <summary>A new monitor: CPU, memory, GPU, one disk tile per drive, network, battery.</summary>
        public static List<MonitorTileConfig> Defaults(IEnumerable<string> fixedDrives)
        {
            var tiles = new List<MonitorTileConfig>
            {
                new() { Kind = MonitorMetrics.Cpu },
                new() { Kind = MonitorMetrics.Memory },
                new() { Kind = MonitorMetrics.Gpu },
            };
            tiles.AddRange(fixedDrives.Select(d => new MonitorTileConfig { Kind = MonitorMetrics.Disk, Drive = d }));
            tiles.Add(new() { Kind = MonitorMetrics.Network });
            tiles.Add(new() { Kind = MonitorMetrics.Battery });
            return tiles;
        }

        /// <summary>Give a monitor from Pickets 1.13 (a list of readings, null meaning all of them)
        /// the same tiles as tile config, a disk reading becoming one tile per drive. New monitors
        /// are created with their tiles. Returns true if anything changed.</summary>
        public static bool Normalize(FenceModel m, IEnumerable<string> fixedDrives)
        {
            if (!m.IsMonitor || m.Tiles != null) return false;
            var drives = fixedDrives.ToList();
            var shown = m.Metrics ?? MonitorMetrics.Original.ToList();
            m.Tiles = new List<MonitorTileConfig>();
            foreach (var kind in MonitorMetrics.Original.Where(shown.Contains))
            {
                if (kind == MonitorMetrics.Disk)
                    m.Tiles.AddRange(drives.Select(d => new MonitorTileConfig { Kind = kind, Drive = d }));
                else
                    m.Tiles.Add(new MonitorTileConfig { Kind = kind });
            }
            m.Metrics = null;
            return true;
        }

        /// <summary>The tile's name: the user's own, or "CPU", "Windows (C:)", "Disk C:".</summary>
        public static string Label(MonitorTileConfig t, string? volumeLabel = null)
        {
            if (!string.IsNullOrWhiteSpace(t.Label)) return t.Label!.Trim();
            if (t.Kind == MonitorMetrics.Disk && !string.IsNullOrEmpty(t.Drive))
                return string.IsNullOrWhiteSpace(volumeLabel) ? $"Disk {t.Drive}" : $"{volumeLabel} ({t.Drive})";
            return MonitorMetrics.Title(t.Kind);
        }

        /// <summary>0 normal, 1 getting high (amber), 2 high (red).</summary>
        /// <param name="value">Percent busy (CPU, GPU, memory), percent of a drive used, or percent
        /// of battery left.</param>
        /// <param name="charging">Battery: plugged in, so a low charge isn't a worry.</param>
        public static int Level(string kind, double value, bool charging = false) => kind switch
        {
            MonitorMetrics.Cpu or MonitorMetrics.Gpu or MonitorMetrics.Memory or MonitorMetrics.Cores =>
                value >= 90 ? 2 : value >= 75 ? 1 : 0,
            MonitorMetrics.Disk => value >= 95 ? 2 : value >= 90 ? 1 : 0,
            MonitorMetrics.Battery => charging ? 0 : value <= 10 ? 2 : value <= 20 ? 1 : 0,
            _ => 0
        };

        /// <summary>How many readings a graph keeps: the graph length over the interval.</summary>
        public static int Capacity(int historyMinutes, int intervalSeconds) =>
            Math.Clamp(historyMinutes * 60 / Math.Max(1, intervalSeconds), 2, 600);
    }
}
