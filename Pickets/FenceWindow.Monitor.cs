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

namespace Pickets
{
    // System monitor fences: live readings (CPU, memory, GPU, disks, network, battery, clock…) as
    // tiles instead of desktop items. They never hold items, so drops, rules and "Move to fence"
    // pass them by. Readings come from Services/SystemMonitor, only for the kinds of tile shown
    // and only while the tiles can be seen. The tiles themselves are FenceModel.Tiles, edited
    // here (fence menu, a tile's right-click menu) or in Settings → System monitor.
    public partial class FenceWindow
    {
        public bool IsMonitor => _model.IsMonitor;

        public ObservableCollection<MonitorTile> MonitorTiles { get; } = new();

        /// <summary>Wired by MainWindow: Edit tiles… opens Settings → System monitor for this fence.</summary>
        public static Action<FenceWindow>? RequestEditMonitorTiles;

        /// <summary>Wired by MainWindow: a Network info tile's Show public IP (on or off).</summary>
        public static Action<bool>? RequestPublicIp;

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

            // A monitor from Pickets 1.13 had a list of readings: turn it into tiles.
            if (MonitorLayout.Normalize(_model, SystemMonitor.FixedDrives().Select(d => d.Name)))
                Dispatcher.BeginInvoke(() => Changed?.Invoke(this, EventArgs.Empty));

            SystemMonitor.Sampled += OnSampled;
            Closed += (_, __) =>
            {
                SystemMonitor.Sampled -= OnSampled;
                SystemMonitor.SetViewing(this, null);
            };
            MonitorTiles.CollectionChanged += (_, __) => { UpdateEmptyHint(); QueueAutoFit(); };
            SyncMonitorTiles(SystemMonitor.Latest);
        }

        /// <summary>The monitor's tiles, in order (Settings edits this list, then calls
        /// <see cref="ApplyMonitorChanges"/>).</summary>
        internal List<MonitorTileConfig> MonitorConfig => _model.Tiles ??= new List<MonitorTileConfig>();

        /// <summary>The tile list changed: show it, and save.</summary>
        internal void ApplyMonitorChanges()
        {
            SyncMonitorTiles(SystemMonitor.Latest);
            UpdateMonitorViewing();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Settings changed the graph length, units or warning colors.</summary>
        internal void ApplyMonitorOptions() => SyncMonitorTiles(SystemMonitor.Latest);

        /// <summary>Read only while the tiles can be seen (not hidden, closed or rolled up), and
        /// only the kinds of reading they show.</summary>
        private void UpdateMonitorViewing()
        {
            if (!IsMonitor) return;
            // A monitor with no tiles has nothing to read, so it doesn't keep the sampler going.
            bool seen = IsVisible && (!_model.Collapsed || _tempExpanded) && MonitorConfig.Count > 0;
            SystemMonitor.SetViewing(this, seen ? MonitorConfig.Select(t => t.Kind).Distinct().ToList() : null);
        }

        /// <summary>For the Pickets window's fence list: "CPU, Memory, Network".</summary>
        public string MonitorSummary
        {
            get
            {
                var names = MonitorConfig.Select(t => MonitorLayout.Label(t)).ToList();
                return names.Count == 0 ? "no tiles" : string.Join(", ", names);
            }
        }

        private void OnSampled(SystemSnapshot snap) => SyncMonitorTiles(snap);

        /// <summary>A tile is left out while this PC can't give its reading: no battery, no GPU
        /// counters, or a drive that isn't there.</summary>
        private static bool CanShow(MonitorTileConfig t, SystemSnapshot? snap) => t.Kind switch
        {
            MonitorMetrics.Disk => snap == null || snap.Drives.Count == 0 || snap.Drives.Any(d => d.Name == t.Drive),
            MonitorMetrics.Battery or MonitorMetrics.Gpu or MonitorMetrics.Cores or MonitorMetrics.DiskIo =>
                SystemMonitor.IsAvailable(t.Kind),
            _ => true
        };

        /// <summary>Make the tiles match the tile list (reusing tiles, so graphs keep their
        /// history), then fill in <paramref name="snap"/>.</summary>
        private void SyncMonitorTiles(SystemSnapshot? snap)
        {
            var wanted = MonitorConfig.Where(t => CanShow(t, snap)).ToList();

            for (int i = MonitorTiles.Count - 1; i >= 0; i--)
                if (!wanted.Any(c => ReferenceEquals(c, MonitorTiles[i].Config))) MonitorTiles.RemoveAt(i);

            var o = Options ?? new AppOptions();
            int capacity = MonitorLayout.Capacity(o.MonitorHistoryMinutes, o.MonitorIntervalSeconds);
            for (int i = 0; i < wanted.Count; i++)
            {
                var config = wanted[i];
                int at = -1;
                for (int j = 0; j < MonitorTiles.Count; j++)
                    if (ReferenceEquals(MonitorTiles[j].Config, config)) { at = j; break; }
                if (at < 0) MonitorTiles.Insert(i, new MonitorTile(config, config.Kind + ":" + config.Drive, ""));
                else if (at != i) MonitorTiles.Move(at, i);

                var tile = MonitorTiles[i];
                var volume = snap?.Drives.FirstOrDefault(d => d.Name == config.Drive)?.Label;
                tile.Label = MonitorLayout.Label(config, volume);
                tile.Capacity = capacity;
                tile.RefreshVisual();
                if (snap != null) UpdateTile(tile, snap, o);
            }
        }

        private static void UpdateTile(MonitorTile tile, SystemSnapshot s, AppOptions o)
        {
            int level = 0;
            switch (tile.Metric)
            {
                case MonitorMetrics.Cpu:
                    tile.Value = $"{s.CpuPercent:0}%";
                    tile.Detail = "Up " + Uptime();
                    tile.Spoken = $"{tile.Label}, {s.CpuPercent:0} percent";
                    tile.Push(s.CpuPercent, 100);
                    level = MonitorLayout.Level(tile.Metric, s.CpuPercent);
                    break;

                case MonitorMetrics.Cores:
                    {
                        if (s.CoreLoads.Count == 0) break;
                        double avg = s.CoreLoads.Average();
                        tile.Value = $"{avg:0}%";
                        tile.Detail = s.CoreLoads.Count == 1 ? "1 core" : $"{s.CoreLoads.Count} logical cores";
                        tile.Spoken = $"{tile.Label}, {avg:0} percent across {s.CoreLoads.Count} cores, busiest {s.CoreLoads.Max():0} percent";
                        tile.Bars = s.CoreLoads.Select(v => v / 100).ToList();
                        level = MonitorLayout.Level(tile.Metric, avg);
                        break;
                    }

                case MonitorMetrics.Memory:
                    {
                        double pct = s.MemoryTotal > 0 ? 100.0 * s.MemoryUsed / s.MemoryTotal : 0;
                        tile.Value = $"{pct:0}%";
                        tile.Detail = $"{SystemMonitor.FormatBytes(s.MemoryUsed)} of {SystemMonitor.FormatBytes(s.MemoryTotal)}";
                        tile.Spoken = $"{tile.Label}, {pct:0} percent used, {tile.Detail}";
                        tile.Push(pct, 100);
                        level = MonitorLayout.Level(tile.Metric, pct);
                        break;
                    }

                case MonitorMetrics.Gpu:
                    {
                        if (s.GpuPercent is not double gpu) break;
                        tile.Value = $"{gpu:0}%";
                        tile.Detail = "Graphics";
                        tile.Spoken = $"{tile.Label}, {gpu:0} percent";
                        tile.Push(gpu, 100);
                        level = MonitorLayout.Level(tile.Metric, gpu);
                        break;
                    }

                case MonitorMetrics.Disk:
                    {
                        var d = s.Drives.FirstOrDefault(x => x.Name == tile.Config.Drive);
                        if (d == null) break;
                        double used = d.TotalBytes > 0 ? 1.0 - (double)d.FreeBytes / d.TotalBytes : 0;
                        tile.Value = $"{SystemMonitor.FormatBytes(d.FreeBytes)} free";
                        tile.Detail = $"{used * 100:0}% of {SystemMonitor.FormatBytes(d.TotalBytes)} used";
                        tile.Spoken = $"{tile.Label}, {SystemMonitor.FormatBytes(d.FreeBytes)} free of {SystemMonitor.FormatBytes(d.TotalBytes)}";
                        tile.Fraction = used;
                        level = MonitorLayout.Level(tile.Metric, used * 100);
                        break;
                    }

                case MonitorMetrics.DiskIo:
                    {
                        if (s.DiskReadBytesPerSec is not double read || s.DiskWriteBytesPerSec is not double write) break;
                        bool bits = o.MonitorNetworkBits;
                        tile.Value = SystemMonitor.FormatRate(read + write, bits);
                        tile.Detail = $"R {SystemMonitor.FormatRate(read, bits)} · W {SystemMonitor.FormatRate(write, bits)}";
                        tile.Spoken = $"{tile.Label}, reading {SystemMonitor.FormatRate(read, bits)}, writing {SystemMonitor.FormatRate(write, bits)}";
                        tile.Push(read + write, null);
                        break;
                    }

                case MonitorMetrics.Network:
                    {
                        bool bits = o.MonitorNetworkBits;
                        tile.Value = "↓ " + SystemMonitor.FormatRate(s.NetDownBytesPerSec, bits);
                        tile.Detail = "↑ " + SystemMonitor.FormatRate(s.NetUpBytesPerSec, bits);
                        tile.Compact = tile.Value + "  " + tile.Detail; // list rows: both directions
                        tile.Spoken = $"{tile.Label}, receiving {SystemMonitor.FormatRate(s.NetDownBytesPerSec, bits)}, " +
                                      $"sending {SystemMonitor.FormatRate(s.NetUpBytesPerSec, bits)}";
                        tile.Push(s.NetDownBytesPerSec + s.NetUpBytesPerSec, null);
                        break;
                    }

                case MonitorMetrics.NetInfo:
                    {
                        tile.Value = s.NetAddress ?? "Offline";
                        if (s.NetAddress == null)
                        {
                            tile.Detail = "No internet connection";
                            tile.Compact = tile.Value;
                            tile.Spoken = $"{tile.Label}, offline";
                            break;
                        }
                        // With Show public IP on, the line under the local address is the public one
                        // (the connection's name is still read out and in the list row's tooltip).
                        string? pub = !o.MonitorPublicIp ? null
                                    : s.PublicAddress ?? (s.PublicAddressFailed ? "unavailable" : "looking up…");
                        tile.Detail = pub == null ? s.NetName ?? "" : "Public " + pub;
                        // Gated on the setting too: turning it off redraws from the last reading,
                        // which may still carry the public address.
                        tile.Compact = o.MonitorPublicIp && s.PublicAddress != null ? $"{tile.Value} · {s.PublicAddress}" : tile.Value;
                        tile.Spoken = $"{tile.Label}, {s.NetName}, local address {s.NetAddress}" +
                                      (pub == null ? "" : $", public address {pub}");
                        break;
                    }

                case MonitorMetrics.Battery:
                    {
                        if (s.Battery is not { } b) break;
                        tile.Value = $"{b.Percent:0}%";
                        tile.Detail = b.Charging ? "Charging"
                                    : b.PluggedIn ? "Plugged in"
                                    : b.SecondsLeft is int left ? SystemMonitor.FormatDuration(left) + " left"
                                    : "On battery";
                        tile.Spoken = $"{tile.Label}, {b.Percent:0} percent, {tile.Detail}";
                        tile.Fraction = b.Percent / 100;
                        level = MonitorLayout.Level(tile.Metric, b.Percent, charging: b.PluggedIn);
                        break;
                    }

                case MonitorMetrics.Clock:
                    {
                        var now = DateTime.Now;
                        tile.Value = now.ToString("t");
                        tile.Detail = $"{now:dddd}, {now.ToString("M")}";
                        tile.Spoken = $"{tile.Label}, {tile.Value}, {tile.Detail}";
                        break;
                    }
            }

            tile.Level = o.MonitorWarnColors ? level : 0;
            if (level > 0)
                tile.Spoken += tile.Metric switch
                {
                    MonitorMetrics.Battery => level == 2 ? ", very low" : ", low",
                    MonitorMetrics.Disk => level == 2 ? ", almost full" : ", nearly full",
                    _ => level == 2 ? ", very high" : ", high"
                };
        }

        private static string Uptime()
        {
            var t = TimeSpan.FromMilliseconds(Environment.TickCount64);
            return t.TotalDays >= 1 ? $"{(int)t.TotalDays} d {t.Hours} h"
                 : t.TotalHours >= 1 ? $"{t.Hours} h {t.Minutes} min"
                 : $"{t.Minutes} min";
        }

        // ---------- Adding, removing and changing tiles ----------

        /// <summary>The fence menu's Add tile submenu: every kind of tile, disk space by drive.
        /// Ones already on the fence, or that this PC can't show, are greyed out.</summary>
        private void FillAddTileMenu(MenuItem parent)
        {
            parent.Items.Clear();
            foreach (var item in AddTileItems(MonitorConfig, AddTile)) parent.Items.Add(item);
        }

        /// <summary>Menu items for adding a tile to <paramref name="tiles"/> (also used by Settings).</summary>
        internal static IEnumerable<MenuItem> AddTileItems(IReadOnlyList<MonitorTileConfig> tiles, Action<MonitorTileConfig> add)
        {
            foreach (var kind in MonitorMetrics.All)
            {
                var mi = new MenuItem { Header = MonitorMetrics.Title(kind), ToolTip = MonitorMetrics.Description(kind) };
                if (kind == MonitorMetrics.Disk)
                {
                    foreach (var d in SystemMonitor.FixedDrives())
                    {
                        string drive = d.Name;
                        var sub = new MenuItem
                        {
                            Header = string.IsNullOrWhiteSpace(d.Label) ? drive : $"{d.Label} ({drive})",
                            IsEnabled = !tiles.Any(t => t.Kind == kind && t.Drive == drive)
                        };
                        sub.Click += (_, __) => add(new MonitorTileConfig { Kind = kind, Drive = drive });
                        mi.Items.Add(sub);
                    }
                    mi.IsEnabled = mi.Items.OfType<MenuItem>().Any(s => s.IsEnabled);
                }
                else if (!SystemMonitor.IsAvailable(kind))
                {
                    mi.Header += " (not on this PC)";
                    mi.IsEnabled = false;
                }
                else
                {
                    mi.IsEnabled = !tiles.Any(t => t.Kind == kind);
                    mi.Click += (_, __) => add(new MonitorTileConfig { Kind = kind });
                }
                yield return mi;
            }
        }

        private void AddTile(MonitorTileConfig config)
        {
            MonitorConfig.Add(config);
            ApplyMonitorChanges();
            Announce($"Added {MonitorLayout.Label(config)}");
        }

        private void RemoveTile(MonitorTile tile)
        {
            if (!MonitorConfig.Remove(tile.Config)) return;
            ApplyMonitorChanges();
            Announce($"Removed {tile.Label}");
        }

        private void MoveTile(MonitorTile tile, int direction)
        {
            var list = MonitorConfig;
            int i = list.IndexOf(tile.Config), j = i + direction;
            if (i < 0 || j < 0 || j >= list.Count) return;
            (list[i], list[j]) = (list[j], list[i]);
            ApplyMonitorChanges();
        }

        private void RenameTile(MonitorTile tile)
        {
            var prompt = new InputDialog("Rename Tile", "Name (leave empty for the usual name):", tile.Label) { Owner = this };
            if (prompt.ShowDialog() != true) return;
            var name = prompt.Value.Trim();
            tile.Config.Label = name.Length == 0 ? null : name;
            ApplyMonitorChanges();
        }

        private void MonitorEditTiles_Click(object sender, RoutedEventArgs e) => RequestEditMonitorTiles?.Invoke(this);

        // ---------- Mouse and keyboard ----------
        private void MonitorTile_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not MonitorTile tile) return;
            (ItemsControl.ContainerFromElement(MonitorItems, fe) as UIElement)?.Focus();
            if (e.ClickCount == 2) { OpenMonitorTool(tile); e.Handled = true; }
        }

        private void MonitorTile_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not MonitorTile tile) return;
            e.Handled = true;
            (ItemsControl.ContainerFromElement(MonitorItems, fe) as UIElement)?.Focus();
            ShowTileMenu(tile, fe);
        }

        /// <summary>A tile's own menu: rename, graph on or off, move, remove, add another.</summary>
        private void ShowTileMenu(MonitorTile tile, UIElement target)
        {
            var menu = new ContextMenu { PlacementTarget = target, Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint };
            void Add(string header, Action action, bool enabled = true)
            {
                var mi = new MenuItem { Header = header, IsEnabled = enabled };
                mi.Click += (_, __) => action();
                menu.Items.Add(mi);
            }

            Add("Open details", () => OpenMonitorTool(tile));
            Add("Rename tile…", () => RenameTile(tile));
            var visual = MonitorMetrics.Visual(tile.Metric);
            if (visual != MonitorVisual.None)
            {
                var graph = new MenuItem
                {
                    Header = visual == MonitorVisual.Bar ? "Show bar" : "Show graph",
                    IsCheckable = true,
                    IsChecked = tile.Config.Graph
                };
                graph.Click += (_, __) => { tile.Config.Graph = !tile.Config.Graph; ApplyMonitorChanges(); };
                menu.Items.Add(graph);
            }
            if (tile.Metric == MonitorMetrics.NetInfo && Options is { } options)
            {
                // The same switch as Settings → System monitor → Show public IP.
                var pub = new MenuItem
                {
                    Header = "Show public IP",
                    IsCheckable = true,
                    IsChecked = options.MonitorPublicIp,
                    ToolTip = "Asks Cloudflare's icanhazip.com for the address the internet sees (it sees your address too)"
                };
                pub.Click += (_, __) => RequestPublicIp?.Invoke(!options.MonitorPublicIp);
                menu.Items.Add(pub);
            }
            menu.Items.Add(new Separator());
            int index = MonitorConfig.IndexOf(tile.Config);
            Add("Move earlier", () => MoveTile(tile, -1), index > 0);
            Add("Move later", () => MoveTile(tile, +1), index >= 0 && index < MonitorConfig.Count - 1);
            menu.Items.Add(new Separator());
            var addTile = new MenuItem { Header = "Add tile" };
            FillAddTileMenu(addTile);
            menu.Items.Add(addTile);
            Add("Edit tiles…", () => RequestEditMonitorTiles?.Invoke(this));
            menu.Items.Add(new Separator());
            var remove = new MenuItem { Header = "Remove tile", InputGestureText = "Delete" };
            remove.SetResourceReference(ForegroundProperty, "Menu.Danger");
            remove.Click += (_, __) => RemoveTile(tile);
            menu.Items.Add(remove);
            _dropMenu = menu; // keeps a hover-opened fence open while the menu is up
            menu.IsOpen = true;
        }

        /// <summary>Task Manager, the drive in Explorer, or the matching Windows settings page.</summary>
        private static void OpenMonitorTool(MonitorTile tile)
        {
            string target = tile.Metric switch
            {
                MonitorMetrics.Disk => tile.Config.Drive + "\\",
                MonitorMetrics.Network or MonitorMetrics.NetInfo => "ms-settings:network-status",
                MonitorMetrics.Battery => "ms-settings:batterysaver",
                MonitorMetrics.Clock => "ms-settings:dateandtime",
                _ => "taskmgr.exe"
            };
            try { Process.Start(new ProcessStartInfo(target) { UseShellExecute = true }); }
            catch (Exception ex) { (System.Windows.Application.Current as App)?.SafeLog("Open " + target, ex); }
        }

        /// <summary>Arrow keys move between tiles, Enter opens the focused one, Delete removes
        /// it and the menu key shows its menu. True if handled.</summary>
        private bool HandleMonitorKey(Key key)
        {
            int count = MonitorTiles.Count;
            if (count == 0) return false;
            int cur = -1;
            for (int i = 0; i < count; i++)
                if (MonitorItems.ItemContainerGenerator.ContainerFromIndex(i) is UIElement { IsKeyboardFocused: true }) cur = i;

            if (key is Key.Enter or Key.Delete or Key.Apps)
            {
                if (cur < 0) return false;
                var tile = MonitorTiles[cur];
                if (key == Key.Enter) OpenMonitorTool(tile);
                else if (key == Key.Delete)
                {
                    RemoveTile(tile);
                    Dispatcher.BeginInvoke(() => FocusMonitorTile(Math.Min(cur, MonitorTiles.Count - 1)),
                                           System.Windows.Threading.DispatcherPriority.Loaded);
                }
                else if (MonitorItems.ItemContainerGenerator.ContainerFromIndex(cur) is UIElement c) ShowTileMenu(tile, c);
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
            FocusMonitorTile(next);
            return true;
        }

        private void FocusMonitorTile(int index)
        {
            if (index >= 0 && MonitorItems.ItemContainerGenerator.ContainerFromIndex(index) is FrameworkElement c)
            {
                c.BringIntoView();
                c.Focus();
            }
            else Scroller.Focus();
        }

        /// <summary>Focus the first tile, so a screen reader reads something.</summary>
        private void FocusFirstMonitorTile() => FocusMonitorTile(0);
    }
}
