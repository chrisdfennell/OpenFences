using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using Pickets.Services;

using MessageBox = Pickets.ThemedMessageBox;
using Button = System.Windows.Controls.Button;

namespace Pickets
{
    // Settings → System monitor: how often monitors read, graph length, units, warning colors,
    // and each monitor's tiles (add, remove, rename, reorder, graph on or off).
    public partial class MainWindow
    {
        /// <summary>One tile in the Settings tile list.</summary>
        public sealed class MonitorTileRow
        {
            public MonitorTileRow(MonitorTileConfig config, int index, int count, string? volumeLabel,
                                  Action<MonitorTileRow, bool> setGraph)
            {
                Config = config;
                _setGraph = setGraph;
                Title = MonitorLayout.Label(config, volumeLabel);
                var visual = MonitorMetrics.Visual(config.Kind);
                var details = new List<string> { MonitorMetrics.Description(config.Kind) };
                if (!string.IsNullOrWhiteSpace(config.Label)) details.Insert(0, MonitorMetrics.Title(config.Kind));
                if (!SystemMonitor.IsAvailable(config.Kind)) details.Add("not available on this PC");
                Details = string.Join(" · ", details);
                GraphVisibility = visual == MonitorVisual.None ? Visibility.Collapsed : Visibility.Visible;
                GraphWord = visual == MonitorVisual.Bar ? "Bar" : "Graph";
                GraphName = $"Show {GraphWord.ToLowerInvariant()} for {Title}";
                // Hidden rather than greyed out, so the buttons still line up.
                UpVisibility = index > 0 ? Visibility.Visible : Visibility.Hidden;
                DownVisibility = index < count - 1 ? Visibility.Visible : Visibility.Hidden;
            }

            public MonitorTileConfig Config { get; }
            public string Title { get; }
            public string Details { get; }
            // Bound two-way: a click and a screen reader's toggle both land here.
            private readonly Action<MonitorTileRow, bool> _setGraph;
            public bool Graph
            {
                get => Config.Graph;
                set { if (value != Config.Graph) _setGraph(this, value); }
            }
            public Visibility GraphVisibility { get; }
            public string GraphWord { get; }
            public string GraphName { get; }
            public Visibility UpVisibility { get; }
            public Visibility DownVisibility { get; }
            public string MoveUpName => "Move " + Title + " up";
            public string MoveDownName => "Move " + Title + " down";
            public string RenameName => "Rename " + Title;
            public string RemoveName => "Remove " + Title;
        }

        // The monitor whose tiles Settings shows.
        private FenceWindow? _monitorTarget;

        private IEnumerable<FenceWindow> MonitorWindows => _openWindows.Where(w => w.IsMonitor);

        private void InitMonitorSettings()
        {
            SystemMonitor.SetInterval(_config.Options.MonitorIntervalSeconds);
            SystemMonitor.PublicIpEnabled = _config.Options.MonitorPublicIp;
            FenceWindow.RequestEditMonitorTiles = EditMonitorTiles;
            // A Network info tile's own Show public IP switch.
            FenceWindow.RequestPublicIp = on =>
            {
                _config.Options.MonitorPublicIp = on;
                SystemMonitor.PublicIpEnabled = on;
                ApplyMonitorOptionsToAll();
                SaveConfig();
                RefreshSettingsPage(); // the switch on the Settings page
            };
        }

        /// <summary>A fence's Edit tiles…: Settings → System monitor, showing that fence.</summary>
        private void EditMonitorTiles(FenceWindow w)
        {
            RestoreFromTray();
            _monitorTarget = w;
            if (NavSettings.IsChecked == true) RefreshMonitorSettings();
            else NavSettings.IsChecked = true; // refreshes the page
            Dispatcher.BeginInvoke(() => MonitorSection.BringIntoView(new Rect(0, 0, 1, 600)),
                                   System.Windows.Threading.DispatcherPriority.Loaded);
        }

        private static string Seconds(int s) => s == 1 ? "1 second" : $"{s} seconds";
        private static string Minutes(int m) => m == 1 ? "1 minute" : $"{m} minutes";

        private void RefreshMonitorSettings()
        {
            if (MonitorTileList == null) return;
            var o = _config.Options;
            MonitorIntervalButton.Content = Seconds(o.MonitorIntervalSeconds) + "  ▾";
            MonitorHistoryButton.Content = Minutes(o.MonitorHistoryMinutes) + "  ▾";

            var monitors = MonitorWindows.ToList();
            if (_monitorTarget == null || !monitors.Contains(_monitorTarget)) _monitorTarget = monitors.FirstOrDefault();

            bool any = _monitorTarget != null;
            MonitorPickButton.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            MonitorPickButton.Content = (_monitorTarget?.FenceName ?? "") + (monitors.Count > 1 ? "  ▾" : "");
            MonitorPickButton.IsEnabled = monitors.Count > 1;
            MonitorAddRow.Visibility = MonitorAddDivider.Visibility = any ? Visibility.Visible : Visibility.Collapsed;
            MonitorTilesHint.Text = !any ? "You don't have a system monitor yet. Add one with New system monitor."
                : monitors.Count > 1 ? "Choose a monitor, then add, remove, rename and reorder its tiles."
                : "Add, remove, rename and reorder this monitor's tiles.";

            if (_monitorTarget == null) { MonitorTileList.ItemsSource = null; return; }
            var tiles = _monitorTarget.MonitorConfig;
            var drives = SystemMonitor.FixedDrives();
            MonitorTileList.ItemsSource = tiles
                .Select((t, i) => new MonitorTileRow(t, i, tiles.Count, drives.FirstOrDefault(d => d.Name == t.Drive)?.Label, SetTileGraph))
                .ToList();
        }

        /// <summary>Settings changed something every monitor uses (graph length, units, colors).</summary>
        private void ApplyMonitorOptionsToAll()
        {
            foreach (var w in MonitorWindows) w.ApplyMonitorOptions();
        }

        // A dropdown like the theme picker: a menu under the button.
        private void ShowChoices<T>(Button button, IEnumerable<T> values, T current, Func<T, string> label, Action<T> pick)
        {
            var menu = new ContextMenu { PlacementTarget = button, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var v in values)
            {
                var value = v;
                var item = new MenuItem { Header = label(value), IsCheckable = true, IsChecked = Equals(value, current) };
                item.Click += (_, __) => pick(value);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void MonitorInterval_Click(object sender, RoutedEventArgs e) =>
            ShowChoices(MonitorIntervalButton, new[] { 1, 2, 5, 10 }, _config.Options.MonitorIntervalSeconds, Seconds, s =>
            {
                _config.Options.MonitorIntervalSeconds = s;
                SystemMonitor.SetInterval(s);
                ApplyMonitorOptionsToAll(); // the same graph length is now more or fewer readings
                SaveConfig();
                RefreshMonitorSettings();
            });

        private void MonitorHistory_Click(object sender, RoutedEventArgs e) =>
            ShowChoices(MonitorHistoryButton, new[] { 1, 2, 5, 10 }, _config.Options.MonitorHistoryMinutes, Minutes, m =>
            {
                _config.Options.MonitorHistoryMinutes = m;
                ApplyMonitorOptionsToAll();
                SaveConfig();
                RefreshMonitorSettings();
            });

        private void MonitorPick_Click(object sender, RoutedEventArgs e) =>
            ShowChoices(MonitorPickButton, MonitorWindows.ToList(), _monitorTarget!, w => w.FenceName, w =>
            {
                _monitorTarget = w;
                RefreshMonitorSettings();
            });

        // ---------- Tiles ----------
        private void ChangeTiles(Action<List<MonitorTileConfig>> change)
        {
            if (_monitorTarget is not { } w) return;
            change(w.MonitorConfig);
            w.ApplyMonitorChanges(); // shows the change on the fence and saves
            RefreshMonitorSettings();
        }

        private static MonitorTileRow? RowOf(object sender) => (sender as FrameworkElement)?.Tag as MonitorTileRow;

        // After the binding finishes: the change rebuilds the list the switch lives in.
        private void SetTileGraph(MonitorTileRow row, bool on) =>
            Dispatcher.BeginInvoke(() => ChangeTiles(_ => row.Config.Graph = on));

        private void MonitorTileUp_Click(object sender, RoutedEventArgs e) => MoveTile(RowOf(sender), -1);
        private void MonitorTileDown_Click(object sender, RoutedEventArgs e) => MoveTile(RowOf(sender), +1);

        private void MoveTile(MonitorTileRow? row, int direction)
        {
            if (row == null) return;
            ChangeTiles(list =>
            {
                int i = list.IndexOf(row.Config), j = i + direction;
                if (i >= 0 && j >= 0 && j < list.Count) (list[i], list[j]) = (list[j], list[i]);
            });
        }

        private void MonitorTileRename_Click(object sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is not { } row) return;
            var prompt = new InputDialog("Rename Tile", "Name (leave empty for the usual name):", row.Title) { Owner = this };
            if (prompt.ShowDialog() != true) return;
            var name = prompt.Value.Trim();
            ChangeTiles(_ => row.Config.Label = name.Length == 0 ? null : name);
        }

        private void MonitorTileRemove_Click(object sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is { } row) ChangeTiles(list => list.Remove(row.Config));
        }

        private void MonitorAdd_Click(object sender, RoutedEventArgs e)
        {
            if (_monitorTarget is not { } w) return;
            var menu = new ContextMenu { PlacementTarget = MonitorAddButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (var item in FenceWindow.AddTileItems(w.MonitorConfig, config => ChangeTiles(list => list.Add(config))))
                menu.Items.Add(item);
            menu.IsOpen = true;
        }

        private void MonitorReset_Click(object sender, RoutedEventArgs e)
        {
            if (_monitorTarget is not { } w) return;
            if (MessageBox.Show(this, $"Put the usual tiles back on “{w.FenceName}”?\n\nYour own tile names and choices for this monitor are lost.",
                                "Reset tiles", MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            ChangeTiles(list =>
            {
                list.Clear();
                list.AddRange(MonitorLayout.Defaults(SystemMonitor.FixedDrives().Select(d => d.Name)));
            });
        }
    }
}
