using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Pickets.Services;

using MessageBox = System.Windows.MessageBox;
using Button = System.Windows.Controls.Button;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using Brush = System.Windows.Media.Brush;
using Color = System.Windows.Media.Color;
using Brushes = System.Windows.Media.Brushes;
using TextBox = System.Windows.Controls.TextBox;

namespace Pickets
{
    // The main window's pages: Home (fence list + quick actions), Settings and About.
    public partial class MainWindow
    {
        /// <summary>One row in the Home page's fence list.</summary>
        public sealed class FenceRow
        {
            public FenceRow(FenceWindow window) => Window = window;
            public FenceWindow Window { get; }
            public string Name { get; init; } = "";
            public string Details { get; init; } = "";
            public Brush Swatch { get; init; } = Brushes.Gray;
            public string State { get; init; } = "";
            public Brush StateBrush { get; init; } = Brushes.Transparent;
            public string ShowLabel { get; init; } = "";
        }

        /// <summary>One saved layout on the Settings page.</summary>
        public sealed class LayoutRow
        {
            public string Title { get; init; } = "";
            public string Details { get; init; } = "";
            public object? Snapshot { get; init; }
        }

        private void InitHub()
        {
            // The switch shows the real state (the registry), even if config disagrees.
            _config.Options.RunAtStartup = _config.Options.RunAtStartup || StartupHelper.IsRunAtStartupEnabled();
            SettingsPage.DataContext = _config.Options;

            var version = "Version " + UpdateService.CurrentVersion.ToString(3);
            SidebarVersion.Text = version;
            VersionText.Text = version;
            AboutVersion.Text = version;

            StateChanged += (_, __) =>
            {
                // Maximized borderless windows overhang the screen slightly; keep content inside.
                if (Content is FrameworkElement root)
                    root.Margin = WindowState == WindowState.Maximized ? new Thickness(7) : new Thickness(0);
                MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
                MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
            };

            RestoreWindowPlacement();
            Closing += (_, __) => RememberWindowPlacement();

            RefreshSettingsPage();
            RefreshHome();
        }

        // ---------- Window placement ----------
        // Reopen where it was (if that's still on a screen), at the same size and page.
        private void RestoreWindowPlacement()
        {
            var o = _config.Options;
            if (o.MainWidth is double w && o.MainHeight is double h && o.MainLeft is double l && o.MainTop is double t)
            {
                var r = ScreenLayout.FitOnScreen(new Rect(l, t, Math.Max(MinWidth, w), Math.Max(MinHeight, h)));
                WindowStartupLocation = WindowStartupLocation.Manual;
                Left = r.Left; Top = r.Top; Width = r.Width; Height = r.Height;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }
            if (o.MainMaximized) WindowState = WindowState.Maximized;

            switch (o.MainPage)
            {
                case "Settings": NavSettings.IsChecked = true; break;
                case "About": NavAbout.IsChecked = true; break;
            }
        }

        private void RememberWindowPlacement()
        {
            var o = _config.Options;
            var r = RestoreBounds.IsEmpty ? new Rect(Left, Top, Width, Height) : RestoreBounds;
            o.MainLeft = r.Left; o.MainTop = r.Top; o.MainWidth = r.Width; o.MainHeight = r.Height;
            o.MainMaximized = WindowState == WindowState.Maximized;
            o.MainPage = NavSettings.IsChecked == true ? "Settings" : NavAbout.IsChecked == true ? "About" : "Home";
        }

        // ---------- Window buttons ----------
        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized; // → tray
        private void MaximizeButton_Click(object sender, RoutedEventArgs e) =>
            WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        private void CloseButton_Click(object sender, RoutedEventArgs e) => Close();

        // ---------- Navigation ----------
        private void Nav_Checked(object sender, RoutedEventArgs e)
        {
            if (HomePage == null) return; // during InitializeComponent
            var page = (sender as FrameworkElement)?.Tag as string;
            HomePage.Visibility = page == "Home" ? Visibility.Visible : Visibility.Collapsed;
            SettingsPage.Visibility = page == "Settings" ? Visibility.Visible : Visibility.Collapsed;
            AboutPage.Visibility = page == "About" ? Visibility.Visible : Visibility.Collapsed;
            PageScroller.ScrollToTop();

            if (page == "Home") RefreshHome();
            if (page == "Settings") RefreshSettingsPage();
        }

        private void Link_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not string url) return;
            try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { /* ignore */ }
        }

        // ---------- Home ----------
        private bool _homeRefreshQueued;

        // Many changes save the config; refresh the list once things settle.
        private void QueueHomeRefresh()
        {
            if (_homeRefreshQueued || FenceList == null) return;
            _homeRefreshQueued = true;
            Dispatcher.BeginInvoke(() => { _homeRefreshQueued = false; RefreshHome(); },
                                   System.Windows.Threading.DispatcherPriority.Background);
        }

        private static readonly Brush DefaultSwatch = Frozen(Color.FromRgb(0x4A, 0x53, 0x66));
        private static readonly Brush ShownBrush = Frozen(Color.FromRgb(0x14, 0x53, 0x2D));
        private static readonly Brush ClosedBrush = Frozen(Color.FromRgb(0x2A, 0x30, 0x3C));
        private static readonly Brush RolledBrush = Frozen(Color.FromRgb(0x1B, 0x2A, 0x4A));

        private static Brush Frozen(Color c)
        {
            var b = new SolidColorBrush(c);
            b.Freeze();
            return b;
        }

        private void RefreshHome()
        {
            if (FenceList == null) return;

            var rows = _openWindows
                .OrderBy(w => w.IsClosedByUser)
                .ThenBy(w => w.FenceName, StringComparer.CurrentCultureIgnoreCase)
                .Select(w =>
                {
                    string state; Brush stateBrush;
                    if (w.IsClosedByUser) { state = "Closed"; stateBrush = ClosedBrush; }
                    else if (_peeked) { state = "Hidden"; stateBrush = ClosedBrush; }
                    else if (w.IsCollapsed) { state = "Rolled up"; stateBrush = RolledBrush; }
                    else { state = "Shown"; stateBrush = ShownBrush; }

                    Brush swatch = DefaultSwatch;
                    if (!string.IsNullOrEmpty(w.AccentColor))
                    {
                        try { swatch = (Brush)new BrushConverter().ConvertFromString(w.AccentColor)!; } catch { /* keep default */ }
                    }

                    int n = w.ItemCount;
                    string items = n == 1 ? "1 item" : $"{n} items";
                    return new FenceRow(w)
                    {
                        Name = w.FenceName,
                        Details = w.IsPortal ? $"Folder portal · {items} · {w.FolderPath}"
                                             : w.TabCount > 0 ? $"{items} · {w.TabCount} tabs" : items,
                        Swatch = swatch,
                        State = state,
                        StateBrush = stateBrush,
                        ShowLabel = w.IsClosedByUser ? "Open" : "Close"
                    };
                })
                .ToList();
            FenceList.ItemsSource = rows;

            int fences = rows.Count;
            int totalItems = _openWindows.Where(w => !w.IsPortal).Sum(w => w.ItemCount);
            int closed = _openWindows.Count(w => w.IsClosedByUser);
            SummaryText.Text = (fences == 1 ? "1 fence" : $"{fences} fences") +
                               $" · {totalItems} desktop items" +
                               (closed > 0 ? $" · {closed} closed" : "");

            ToggleAllButton.Content = _peeked ? "Show all fences" : "Hide all fences";
            SearchTileHint.Text = _config.Options.GlobalHotkeys
                ? $"Find any item ({_config.Options.SearchHotkey})"
                : "Find any item in any fence";
        }

        private void ToggleAllFences_Click(object sender, RoutedEventArgs e)
        {
            TogglePeek();
            RefreshHome();
        }

        private static FenceWindow? RowWindow(object sender) => ((sender as FrameworkElement)?.Tag as FenceRow)?.Window;

        private void FenceRow_ShowClose_Click(object sender, RoutedEventArgs e)
        {
            if (RowWindow(sender) is not { } w) return;
            if (w.IsClosedByUser) w.Reopen();
            else w.CloseFence();
            SaveConfig();
        }

        private void FenceRow_Locate_Click(object sender, RoutedEventArgs e)
        {
            if (RowWindow(sender) is not { } w) return;
            if (w.IsClosedByUser) { w.Reopen(); SaveConfig(); }
            w.Locate();
        }

        private void FenceRow_Delete_Click(object sender, RoutedEventArgs e)
        {
            RowWindow(sender)?.RequestDelete();
            RefreshHome();
        }

        // ---------- Settings ----------
        private bool _lastShowThumbnails, _lastHideExtensions;

        private void RefreshSettingsPage()
        {
            if (SettingsPage == null) return;
            // Re-bind so every switch shows the current values.
            SettingsPage.DataContext = null;
            SettingsPage.DataContext = _config.Options;
            _lastShowThumbnails = _config.Options.ShowThumbnails;
            _lastHideExtensions = _config.Options.HideFileExtensions;

            SearchHotkeyBox.Text = _config.Options.SearchHotkey;
            ToggleHotkeyBox.Text = _config.Options.ToggleFencesHotkey;
            UpdateHotkeyStatus();
            RefreshLayouts();
        }

        // Every switch on the Settings page: the binding has already updated the option.
        private void Setting_Changed(object sender, RoutedEventArgs e)
        {
            var o = _config.Options;

            StartupHelper.SetRunAtStartup(o.RunAtStartup);
            if (!o.DoubleClickPeekFences && _peeked) SetFencesHidden(false); // un-peek when turning it off
            ApplyDoubleClickSetting();
            ApplyAutoOrganizeSetting();
            ApplyHotkeySetting();
            UpdateHotkeyStatus();

            if (o.ShowThumbnails != _lastShowThumbnails || o.HideFileExtensions != _lastHideExtensions)
            {
                _lastShowThumbnails = o.ShowThumbnails;
                _lastHideExtensions = o.HideFileExtensions;
                foreach (var w in _openWindows) w.RefreshItems();
            }

            SaveConfig();
        }

        private void UpdateHotkeyStatus()
        {
            bool problem = _config.Options.GlobalHotkeys && _hotkeyProblems.Count > 0;
            HotkeyStatus.Text = string.Join("  ", _hotkeyProblems) + (problem ? " Pick a different shortcut." : "");
            HotkeyStatus.Visibility = problem ? Visibility.Visible : Visibility.Collapsed;
            SearchHotkeyBox.IsEnabled = ToggleHotkeyBox.IsEnabled = _config.Options.GlobalHotkeys;
        }

        // ---- Shortcut recorder: click the box, press the new combination (Esc cancels) ----
        private void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is TextBox box) box.Text = "Press a shortcut…";
        }

        private void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
        {
            if (sender is not TextBox box) return;
            box.Text = Equals(box.Tag, "Search") ? _config.Options.SearchHotkey : _config.Options.ToggleFencesHotkey;
        }

        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (sender is not TextBox box) return;
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;
            if (key == Key.Escape) { Keyboard.ClearFocus(); return; }

            var text = HotkeyText.Format(Keyboard.Modifiers, key);
            if (text == null) return; // still holding modifiers, or no modifier yet

            if (Equals(box.Tag, "Search")) _config.Options.SearchHotkey = text;
            else _config.Options.ToggleFencesHotkey = text;

            ApplyHotkeySetting();
            UpdateHotkeyStatus();
            SaveConfig();
            Keyboard.ClearFocus(); // shows the saved shortcut
        }

        // ---- Layouts ----
        private void RefreshLayouts()
        {
            var rows = LayoutSnapshots.List().Select(s => new LayoutRow
            {
                Title = s.Name,
                Details = (s.Automatic ? "Saved automatically · " : "") +
                          $"{s.Created:MMM d, yyyy h:mm tt} · {s.Fences.Count} fences",
                Snapshot = s
            }).ToList();
            LayoutList.ItemsSource = rows;
            NoLayoutsText.Visibility = rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SaveLayout_Click(object sender, RoutedEventArgs e)
        {
            SaveLayoutSnapshot();
            RefreshLayouts();
        }

        private void LayoutRestore_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is LayoutSnapshots.Snapshot snap)
            {
                RestoreLayoutSnapshot(snap);
                RefreshLayouts();
            }
        }

        private void LayoutDelete_Click(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is not LayoutSnapshots.Snapshot snap) return;
            if (MessageBox.Show($"Delete the saved layout “{snap.Name}”?", "Layouts",
                                MessageBoxButton.OKCancel, MessageBoxImage.Question) != MessageBoxResult.OK) return;
            LayoutSnapshots.Delete(snap);
            RefreshLayouts();
        }

        private void OpenLayoutsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(LayoutSnapshots.Folder);
                Process.Start(new ProcessStartInfo { FileName = LayoutSnapshots.Folder, UseShellExecute = true });
            }
            catch { /* ignore */ }
        }
    }
}
