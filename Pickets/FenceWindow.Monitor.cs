using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Pickets.Controls;
using Pickets.Services;

using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Pickets
{
    // System monitor fences: live readings (CPU, memory, disk space, network, battery) as tiles
    // instead of desktop items. They never hold items, so drops, rules and "Move to fence" pass
    // them by. Readings come from Services/SystemMonitor and only while the tiles can be seen.
    public partial class FenceWindow
    {
        public bool IsMonitor => _model.IsMonitor;

        public ObservableCollection<MonitorTile> MonitorTiles { get; } = new();

        // ----- Layout metrics bound by the XAML (follow the fence's icon size) -----
        public double MonitorTileWidth => _model.IconSize switch
        {
            FenceIconSize.Small => 124,
            FenceIconSize.Large => 196,
            _ => 156
        };
        public double MonitorValueSize => _model.IconSize switch
        {
            FenceIconSize.Small => 17,
            FenceIconSize.Large => 28,
            _ => 22
        };
        public double MonitorGraphHeight => _model.IconSize switch
        {
            FenceIconSize.Small => 22,
            FenceIconSize.Large => 40,
            _ => 30
        };

        private void InitMonitor()
        {
            AllowDrop = false;
            Items.Visibility = Visibility.Collapsed;
            MonitorItems.Visibility = Visibility.Visible;
            MonitorItems.ItemsSource = MonitorTiles;
            System.Windows.Automation.AutomationProperties.SetName(MonitorItems, _model.Name);

            SystemMonitor.Sampled += OnSampled;
            Closed += (_, __) =>
            {
                SystemMonitor.Sampled -= OnSampled;
                SystemMonitor.SetViewing(this, false);
            };
            MonitorTiles.CollectionChanged += (_, __) => { UpdateEmptyHint(); QueueAutoFit(); };
            if (SystemMonitor.Latest is { } latest) OnSampled(latest);
            else SyncMonitorTiles(null);
        }

        /// <summary>Read only while the tiles can be seen: not hidden, closed or rolled up.</summary>
        private void UpdateMonitorViewing()
        {
            if (!IsMonitor) return;
            SystemMonitor.SetViewing(this, IsVisible && (!_model.Collapsed || _tempExpanded));
        }

        private bool ShowsMetric(string metric) => _model.Metrics == null || _model.Metrics.Contains(metric);

        /// <summary>For the Pickets window's fence list: "CPU, Memory, Network".</summary>
        public string MonitorSummary
        {
            get
            {
                var shown = MonitorMetrics.All.Where(ShowsMetric).Select(MonitorMetrics.Title).ToList();
                return shown.Count == 0 ? "nothing chosen" : string.Join(", ", shown);
            }
        }

        private void MonitorMetric_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi || mi.Tag is not string metric) return;
            var list = _model.Metrics ?? MonitorMetrics.All.ToList();
            if (list.Contains(metric)) list.Remove(metric);
            else list.Add(metric);
            // Keep the menu's order, and go back to "everything" (which picks up new readings later).
            list = MonitorMetrics.All.Where(list.Contains).ToList();
            _model.Metrics = list.Count == MonitorMetrics.All.Length ? null : list;
            SyncMonitorTiles(SystemMonitor.Latest);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void OnSampled(SystemSnapshot snap) => SyncMonitorTiles(snap);

        /// <summary>Make the tiles match the chosen readings (one disk tile per drive, battery only
        /// when there is one), then fill in <paramref name="snap"/>.</summary>
        private void SyncMonitorTiles(SystemSnapshot? snap)
        {
            var wanted = new List<(string metric, string key, string label)>();
            foreach (var m in MonitorMetrics.All.Where(ShowsMetric))
            {
                if (m == MonitorMetrics.Disk)
                {
                    foreach (var d in snap?.Drives ?? Array.Empty<DriveSample>())
                        wanted.Add((m, "disk:" + d.Name, DriveLabel(d)));
                }
                else if (m == MonitorMetrics.Battery)
                {
                    if (snap?.Battery != null) wanted.Add((m, m, MonitorMetrics.Title(m)));
                }
                else wanted.Add((m, m, MonitorMetrics.Title(m)));
            }

            // Drop tiles no longer wanted, then put the wanted ones in order (reusing existing
            // tiles so their graphs keep their history).
            for (int i = MonitorTiles.Count - 1; i >= 0; i--)
                if (!wanted.Any(w => w.key == MonitorTiles[i].Key)) MonitorTiles.RemoveAt(i);
            for (int i = 0; i < wanted.Count; i++)
            {
                var (metric, key, label) = wanted[i];
                int at = IndexOfTile(key);
                if (at < 0) MonitorTiles.Insert(i, new MonitorTile(metric, key, label));
                else if (at != i) MonitorTiles.Move(at, i);
                MonitorTiles[i].Label = label;
            }

            if (snap != null)
                foreach (var tile in MonitorTiles) UpdateTile(tile, snap);
        }

        private int IndexOfTile(string key)
        {
            for (int i = 0; i < MonitorTiles.Count; i++)
                if (MonitorTiles[i].Key == key) return i;
            return -1;
        }

        private static string DriveLabel(DriveSample d) =>
            string.IsNullOrWhiteSpace(d.Label) ? $"Disk {d.Name}" : $"{d.Label} ({d.Name})";

        private static void UpdateTile(MonitorTile tile, SystemSnapshot s)
        {
            switch (tile.Metric)
            {
                case MonitorMetrics.Cpu:
                    tile.Value = $"{s.CpuPercent:0}%";
                    tile.Detail = "Up " + Uptime();
                    tile.Spoken = $"CPU, {s.CpuPercent:0} percent";
                    tile.Push(s.CpuPercent, 100);
                    break;

                case MonitorMetrics.Memory:
                    {
                        double pct = s.MemoryTotal > 0 ? 100.0 * s.MemoryUsed / s.MemoryTotal : 0;
                        tile.Value = $"{pct:0}%";
                        tile.Detail = $"{SystemMonitor.FormatBytes(s.MemoryUsed)} of {SystemMonitor.FormatBytes(s.MemoryTotal)}";
                        tile.Spoken = $"Memory, {pct:0} percent used, {tile.Detail}";
                        tile.Push(pct, 100);
                        break;
                    }

                case MonitorMetrics.Disk:
                    {
                        var d = s.Drives.FirstOrDefault(x => "disk:" + x.Name == tile.Key);
                        if (d == null) break;
                        double used = d.TotalBytes > 0 ? 1.0 - (double)d.FreeBytes / d.TotalBytes : 0;
                        tile.Value = $"{SystemMonitor.FormatBytes(d.FreeBytes)} free";
                        tile.Detail = $"{used * 100:0}% of {SystemMonitor.FormatBytes(d.TotalBytes)} used";
                        tile.Spoken = $"{tile.Label}, {SystemMonitor.FormatBytes(d.FreeBytes)} free of {SystemMonitor.FormatBytes(d.TotalBytes)}";
                        tile.Fraction = used;
                        break;
                    }

                case MonitorMetrics.Network:
                    tile.Value = "↓ " + SystemMonitor.FormatRate(s.NetDownBytesPerSec);
                    tile.Detail = "↑ " + SystemMonitor.FormatRate(s.NetUpBytesPerSec);
                    tile.Spoken = $"Network, receiving {SystemMonitor.FormatRate(s.NetDownBytesPerSec)}, " +
                                  $"sending {SystemMonitor.FormatRate(s.NetUpBytesPerSec)}";
                    tile.Push(s.NetDownBytesPerSec + s.NetUpBytesPerSec, null);
                    break;

                case MonitorMetrics.Battery:
                    {
                        if (s.Battery is not { } b) break;
                        tile.Value = $"{b.Percent:0}%";
                        tile.Detail = b.Charging ? "Charging"
                                    : b.PluggedIn ? "Plugged in"
                                    : b.SecondsLeft is int left ? SystemMonitor.FormatDuration(left) + " left"
                                    : "On battery";
                        tile.Spoken = $"Battery, {b.Percent:0} percent, {tile.Detail}";
                        tile.Fraction = b.Percent / 100;
                        break;
                    }
            }
        }

        private static string Uptime()
        {
            var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h"
                 : t.TotalHours >= 1 ? $"{t.Hours} h {t.Minutes} min"
                 : $"{t.Minutes} min";
        }

        // ---------- Opening the matching Windows tool ----------
        private void MonitorTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not MonitorTile tile) return;
            (ItemsControl.ContainerFromElement(MonitorItems, fe) as UIElement)?.Focus();
            if (e.ClickCount == 2) { OpenMonitorTool(tile); e.Handled = true; }
        }

        /// <summary>Task Manager for CPU and memory, the drive in Explorer, network or battery settings.</summary>
        private static void OpenMonitorTool(MonitorTile tile)
        {
            string target = tile.Metric switch
            {
                MonitorMetrics.Disk => tile.Key["disk:".Length..] + "\\",
                MonitorMetrics.Network => "ms-settings:network-status",
                MonitorMetrics.Battery => "ms-settings:batterysaver",
                _ => "taskmgr.exe"
            };
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { (System.Windows.Application.Current as App)?.SafeLog("Open " + target, ex); }
        }

        /// <summary>Arrow keys move between tiles, Enter opens the focused one. True if handled.</summary>
        private bool HandleMonitorKey(Key key)
        {
            int count = MonitorTiles.Count;
            if (count == 0) return false;
            int cur = -1;
            for (int i = 0; i < count; i++)
                if (MonitorItems.ItemContainerGenerator.ContainerFromIndex(i) is UIElement { IsKeyboardFocused: true }) cur = i;

            if (key == Key.Enter)
            {
                if (cur < 0) return false;
                OpenMonitorTool(MonitorTiles[cur]);
                return true;
            }

            int cols = IsListView ? 1 : Math.Max(1, (int)(Scroller.ViewportWidth / (MonitorTileWidth + 8)));
            int next = key switch
            {
                Key.Right => cur < 0 ? 0 : Math.Min(count - 1, cur + 1),
                Key.Left => cur < 0 ? 0 : Math.Max(0, cur - 1),
                Key.Down => cur < 0 ? 0 : Math.Min(count - 1, cur + cols),
                Key.Up => cur < 0 ? 0 : Math.Max(0, cur - cols),
                Key.Home => 0,
                Key.End => count - 1,
                _ => -1
            };
            if (next < 0) return false;
            if (MonitorItems.ItemContainerGenerator.ContainerFromIndex(next) is FrameworkElement c)
            {
                c.BringIntoView();
                c.Focus();
            }
            return true;
        }

        /// <summary>Focus the first tile, so a screen reader reads something.</summary>
        private void FocusFirstMonitorTile()
        {
            if (MonitorItems.ItemContainerGenerator.ContainerFromIndex(0) is UIElement c) c.Focus();
            else Scroller.Focus();
        }
    }
}
