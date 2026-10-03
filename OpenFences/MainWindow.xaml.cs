using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using OpenFences.Services;

// Aliases to avoid WinForms/WPF ambiguity
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using MessageBox = System.Windows.MessageBox;

namespace OpenFences
{
    public partial class MainWindow : Window
    {
        // ---------- Paths & state ----------
        private readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenFences", "config.json");

        private AppConfig _config = new();                // holds Fences + Options
        private List<FenceModel> _fences => _config.Fences;

        private readonly List<FenceWindow> _openWindows = new();

        // Tray
        private WinForms.NotifyIcon? _tray;
        private WinForms.ContextMenuStrip? _trayMenu;

        public MainWindow()
        {
            InitializeComponent();

            // Desktop host handles (WorkerW/Progman/DefView)
            DesktopHelper.InitializeDesktopHandles();

            // Load or create config
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            LoadConfig();

            // Migrate legacy name-based ownership (IconNames) → path-based (ItemPaths).
            MigrateLegacyOwnership();

            // Initialize settings checkboxes from config + system (fully-qualify WPF CheckBox)
            if (FindName("ChkRunAtStartup") is System.Windows.Controls.CheckBox chkRun)
                chkRun.IsChecked = _config.Options.RunAtStartup || StartupHelper.IsRunAtStartupEnabled();

            if (FindName("ChkHideIconsOnStart") is System.Windows.Controls.CheckBox chkHide)
                chkHide.IsChecked = _config.Options.HideIconsOnStartup;

            if (FindName("ChkDoubleClickDesktop") is System.Windows.Controls.CheckBox chkDbl)
                chkDbl.IsChecked = _config.Options.DoubleClickDesktopToToggleIcons;

            // Apply settings effects at startup
            if (_config.Options.RunAtStartup || StartupHelper.IsRunAtStartupEnabled())
                StartupHelper.SetRunAtStartup(true);

            if (_config.Options.HideIconsOnStartup)
                DesktopHelper.SetDesktopIconsVisible(false);

            // Double-click-empty-desktop behavior (toggle icons and/or peek fences)
            ApplyDoubleClickSetting();

            // Continuous auto-organize of new desktop items
            ApplyAutoOrganizeSetting();

            // Reflect persisted settings in the menu checkmarks
            InitSettingsChecks();

            // Wire checkbox click handlers (so XAML can keep old names if needed)
            WireSettingsHandlers();

            // Cross-fence drops route through here so an item leaves any prior fence.
            FenceWindow.RequestAssignItems = AssignItemsToFence;

            // Spawn fence windows from config
            SpawnFencesFromConfig();

            // Sweep any desktop items not yet owned into the catch-all "Desktop" fence, then
            // hide the real desktop icons — everything is rendered inside fences now.
            BuildCatchAll();
            StartDesktopWatcher();
            DesktopHelper.SetDesktopIconsVisible(false);

            // Tray + minimize-to-tray behavior
            StateChanged += MainWindow_StateChanged;
            InitTrayIcon();

            // Right-click-drag rectangle to create an (empty) fence
            DesktopRightDragFenceSelector.Start(CreateFenceFromRect);

            // Delete/Enter operate on the whole selection across all fences
            FenceWindow.RequestDeleteSelected = DeleteAllSelected;
            FenceWindow.RequestOpenSelected = OpenAllSelected;
            FenceWindow.RequestRemoveSelected = RemoveSelectedFromFences;

            // Left-drag on the empty desktop = lasso that selects items across fences
            DesktopLeftDragLasso.Start(OnLassoUpdate, OnLassoEnd);

            // GitHub release checks (startup + daily, if enabled in Settings)
            StartUpdateChecks();

            // Docking/undocking, resolution or scaling changes: put fences back on screen
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        }

        // ========== Monitor arrangement changes ==========
        private System.Windows.Threading.DispatcherTimer? _displayTimer;

        // Windows raises several of these while monitors settle, so wait for a quiet moment.
        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            Dispatcher.BeginInvoke(() =>
            {
                if (_displayTimer == null)
                {
                    _displayTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1.5) };
                    _displayTimer.Tick += (_, __) =>
                    {
                        _displayTimer.Stop();
                        bool moved = false;
                        foreach (var w in _openWindows.ToList())
                            if (w.FitToCurrentScreens()) moved = true;
                        if (moved) SaveConfig();
                    };
                }
                _displayTimer.Stop();
                _displayTimer.Start();
            });
        }

        // ========== Cross-fence selection (lasso + bulk actions) ==========
        private void OnLassoUpdate(Rect lassoPx, bool additive)
        {
            foreach (var w in _openWindows)
                if (w.IsVisible) w.SelectItemsInScreenRectPx(lassoPx, additive);
        }

        private void OnLassoEnd()
        {
            // Selection stays highlighted. Act on it via Delete/Enter (with a fence focused)
            // or an item's right-click → Delete, which both route through the global handlers.
        }

        private void DeleteAllSelected()
        {
            int total = _openWindows.Sum(w => w.SelectedCount);
            if (total == 0) return;

            if (!FenceWindow.ConfirmDelete(total, offerRemove: _openWindows.Any(w => w.CanRemoveSelected)))
                return;

            var failed = new List<string>();
            foreach (var w in _openWindows.ToList()) failed.AddRange(w.DeleteSelectedItemsNoConfirm());
            FenceWindow.ReportDeleteFailures(failed);
        }

        // "Remove from fence": hand the selected items back to the catch-all "Desktop" fence.
        // Nothing is deleted. Portals mirror a real folder, so they're left alone.
        private void RemoveSelectedFromFences()
        {
            var paths = RealFences
                .Where(w => !IsCatchAll(w))
                .SelectMany(w => w.SelectedPaths)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            if (paths.Count == 0) return;

            AssignItemsToFence(EnsureRealFence(CatchAllFenceName), paths);
        }

        private static bool IsCatchAll(FenceWindow w) =>
            !w.IsPortal && string.Equals(w.FenceName, CatchAllFenceName, StringComparison.OrdinalIgnoreCase);

        private void OpenAllSelected()
        {
            foreach (var w in _openWindows.ToList()) w.OpenSelectedItems();
        }

        private void OnMinimizeClicked(object? sender, RoutedEventArgs e)
            => WindowState = WindowState.Minimized;

        private void OnCloseClicked(object? sender, RoutedEventArgs e)
            => Close();

        // Drag the window when the transparent header pad is grabbed
        private void HeaderDrag_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.LeftButton == System.Windows.Input.MouseButtonState.Pressed)
            {
                try { DragMove(); } catch { /* ignore while maximized etc. */ }
            }
        }

        // Settings → Start with Windows
        private void MiRunAtStartup_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = MiRunAtStartup.IsChecked;
            _config.Options.RunAtStartup = enabled;
            StartupHelper.SetRunAtStartup(enabled);
            SaveConfig();
        }

        // Settings → Hide desktop icons on startup
        private void MiHideIconsOnStart_Click(object sender, RoutedEventArgs e)
        {
            bool hide = MiHideIconsOnStart.IsChecked;
            _config.Options.HideIconsOnStartup = hide;
            SaveConfig();
        }

        // Settings → Double-click empty desktop toggles icons
        private void MiDoubleClickDesktop_Click(object sender, RoutedEventArgs e)
        {
            bool enabled = MiDoubleClickDesktop.IsChecked;
            _config.Options.DoubleClickDesktopToToggleIcons = enabled;
            ApplyDoubleClickSetting();
            SaveConfig();
        }

        // Settings → Double-click empty desktop hides/shows fences (peek)
        private void MiPeekFences_Click(object sender, RoutedEventArgs e)
        {
            _config.Options.DoubleClickPeekFences = MiPeekFences.IsChecked;
            if (!_config.Options.DoubleClickPeekFences && _peeked) TogglePeek(); // un-peek if turning off
            ApplyDoubleClickSetting();
            SaveConfig();
        }

        // Settings → Auto-organize new desktop items into fences
        private void MiAutoOrganize_Click(object sender, RoutedEventArgs e)
        {
            _config.Options.AutoOrganize = MiAutoOrganize.IsChecked;
            ApplyAutoOrganizeSetting();
            SaveConfig();
        }

        // Reflect persisted settings in the menu checkmarks at startup.
        private void InitSettingsChecks()
        {
            MiRunAtStartup.IsChecked = _config.Options.RunAtStartup || StartupHelper.IsRunAtStartupEnabled();
            MiHideIconsOnStart.IsChecked = _config.Options.HideIconsOnStartup;
            MiDoubleClickDesktop.IsChecked = _config.Options.DoubleClickDesktopToToggleIcons;
            MiPeekFences.IsChecked = _config.Options.DoubleClickPeekFences;
            MiAutoOrganize.IsChecked = _config.Options.AutoOrganize;
            MiCheckForUpdates.IsChecked = _config.Options.CheckForUpdates;
        }

        // Settings → Check for updates automatically
        private void MiCheckForUpdates_Click(object sender, RoutedEventArgs e)
        {
            _config.Options.CheckForUpdates = MiCheckForUpdates.IsChecked;
            SaveConfig();
        }

        private void CheckForUpdatesNow_Click(object? sender, RoutedEventArgs? e) => _ = CheckForUpdatesAsync(manual: true);

        // ========== Updates ==========
        private System.Windows.Threading.DispatcherTimer? _updateTimer;
        private bool _updateDialogOpen;

        // First check shortly after launch (so startup isn't slowed), then once a day.
        private void StartUpdateChecks()
        {
            _updateTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _updateTimer.Tick += (_, __) =>
            {
                _updateTimer.Interval = TimeSpan.FromHours(24);
                if (_config.Options.CheckForUpdates) _ = CheckForUpdatesAsync(manual: false);
            };
            _updateTimer.Start();
        }

        private async Task CheckForUpdatesAsync(bool manual)
        {
            if (_updateDialogOpen) return;

            UpdateInfo? update;
            try
            {
                update = await UpdateService.CheckAsync();
            }
            catch (Exception ex)
            {
                // Automatic checks fail quietly (offline, rate-limited…); a manual check says why.
                (System.Windows.Application.Current as App)?.SafeLog("Update check", ex);
                if (manual)
                    MessageBox.Show("Couldn't check for updates:\n" + ex.Message,
                                    "OpenFences", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (update == null)
            {
                if (manual)
                    MessageBox.Show($"You're up to date (version {UpdateService.CurrentVersion.ToString(3)}).",
                                    "OpenFences", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            // Automatic checks respect "Skip this version"; a manual check always offers it.
            if (!manual && string.Equals(_config.Options.SkippedVersion, update.Version.ToString(3), StringComparison.Ordinal))
                return;

            _updateDialogOpen = true;
            UpdateDialog dlg;
            try
            {
                dlg = new UpdateDialog(update);
                if (IsVisible) { dlg.Owner = this; dlg.WindowStartupLocation = WindowStartupLocation.CenterOwner; }
                dlg.ShowDialog();
            }
            finally { _updateDialogOpen = false; }

            switch (dlg.Choice)
            {
                case UpdateDialog.Result.Skip:
                    _config.Options.SkippedVersion = update.Version.ToString(3);
                    SaveConfig();
                    break;

                case UpdateDialog.Result.ReadyToInstall when dlg.MsiPath != null:
                    try
                    {
                        UpdateService.LaunchInstallerAndRestart(dlg.MsiPath);
                    }
                    catch (Exception ex)
                    {
                        (System.Windows.Application.Current as App)?.SafeLog("Update install", ex);
                        MessageBox.Show("The update couldn't be started:\n" + ex.Message,
                                        "OpenFences", MessageBoxButton.OK, MessageBoxImage.Error);
                        return;
                    }
                    // Exit normally so the config is saved and the desktop icons come back; the
                    // helper installs once we're gone and then starts the new version.
                    Close();
                    break;
            }
        }


        // ========== UI header interactions (borderless drag, min/close) ==========
        private void Header_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
                DragMove();
        }
        private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

        // === BRIDGES for older XAML handler names (safe to keep; or update XAML to new names) ===
        private void Header_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
            => Header_MouseLeftButtonDown(sender, e);
        private void ChkRunAtStartup_CheckedChanged(object sender, System.Windows.RoutedEventArgs e)
            => ChkRunAtStartup_Click(sender, e);
        private void ChkHideIconsOnStart_CheckedChanged(object sender, System.Windows.RoutedEventArgs e)
            => ChkHideIconsOnStart_Click(sender, e);
        private void ChkDoubleClickDesktop_CheckedChanged(object sender, System.Windows.RoutedEventArgs e)
            => ChkDoubleClickDesktop_Click(sender, e);

        // ========== Tray ==========
        private void InitTrayIcon()
        {
            _trayMenu = new WinForms.ContextMenuStrip();

            var restore = new WinForms.ToolStripMenuItem("Restore OpenFences", null, (_, __) => RestoreFromTray());
            var newFence = new WinForms.ToolStripMenuItem("New Fence", null, (_, __) => NewFence_Click(null!, null!));
            var newPortal = new WinForms.ToolStripMenuItem("New Folder Portal…", null, (_, __) => NewFolderPortal_Click(null!, null!));
            var showAll = new WinForms.ToolStripMenuItem("Show All Fences", null, (_, __) => ShowAll_Click(null!, null!));
            var hideAll = new WinForms.ToolStripMenuItem("Hide All Fences", null, (_, __) => HideAll_Click(null!, null!));
            var toggle = new WinForms.ToolStripMenuItem("Toggle Desktop Icons", null, (_, __) => ToggleDesktopIcons_Click(null!, null!));
            var updates = new WinForms.ToolStripMenuItem("Check for Updates…", null, (_, __) => CheckForUpdatesNow_Click(null, null));
            var exit = new WinForms.ToolStripMenuItem("Exit", null, (_, __) => Close());

            _trayMenu.Items.Add(restore);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add(newFence);
            _trayMenu.Items.Add(newPortal);
            _trayMenu.Items.Add(showAll);
            _trayMenu.Items.Add(hideAll);
            _trayMenu.Items.Add(toggle);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add(updates);
            _trayMenu.Items.Add(exit);

            _tray = new WinForms.NotifyIcon
            {
                Text = "OpenFences",
                Icon = LoadAppIconOrFallback(),
                Visible = true,
                ContextMenuStrip = _trayMenu
            };
            _tray.DoubleClick += (_, __) => RestoreFromTray();
        }

        private static Drawing.Icon LoadAppIconOrFallback()
        {
            try
            {
                // Load WPF resource (pack URI) ico for the tray
                var uri = new Uri("pack://application:,,,/Assets/open-fence.ico", UriKind.Absolute);
                var s = System.Windows.Application.GetResourceStream(uri)?.Stream;
                // Ask for the small-icon size so the tray gets the crisp 16px (or DPI-scaled) frame
                if (s != null) return new Drawing.Icon(s, WinForms.SystemInformation.SmallIconSize);
            }
            catch { /* fallback below */ }
            return Drawing.SystemIcons.Application;
        }

        private void MainWindow_StateChanged(object? sender, EventArgs e)
        {
            if (WindowState == WindowState.Minimized)
                MinimizeToTray();
        }

        private void MinimizeToTray()
        {
            Hide();
            ShowInTaskbar = false;

            if (_tray is { } ni)
            {
                ni.BalloonTipTitle = "OpenFences";
                ni.BalloonTipText = "Still running. Double-click the tray icon to restore.";
                ni.ShowBalloonTip(1200);
            }
        }

        internal void RestoreFromTray()
        {
            Show();
            WindowState = WindowState.Normal;
            ShowInTaskbar = true;
            Activate();
        }

        // ========== Settings: wire checkbox handlers ==========
        private void WireSettingsHandlers()
        {
            if (FindName("ChkRunAtStartup") is System.Windows.Controls.CheckBox chkRun)
                chkRun.Click += ChkRunAtStartup_Click;

            if (FindName("ChkHideIconsOnStart") is System.Windows.Controls.CheckBox chkHide)
                chkHide.Click += ChkHideIconsOnStart_Click;

            if (FindName("ChkDoubleClickDesktop") is System.Windows.Controls.CheckBox chkDbl)
                chkDbl.Click += ChkDoubleClickDesktop_Click;
        }

        private void ChkRunAtStartup_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox chk) return;
            _config.Options.RunAtStartup = chk.IsChecked == true;
            StartupHelper.SetRunAtStartup(_config.Options.RunAtStartup);
            SaveConfig();
        }

        private void ChkHideIconsOnStart_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox chk) return;
            _config.Options.HideIconsOnStartup = chk.IsChecked == true;
            SaveConfig();
        }

        private void ChkDoubleClickDesktop_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not System.Windows.Controls.CheckBox chk) return;
            _config.Options.DoubleClickDesktopToToggleIcons = chk.IsChecked == true;
            ApplyDoubleClickSetting();
            SaveConfig();
        }

        // ========== Config I/O ==========
        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        // SaveConfig keeps the previous good config here, so a damaged config.json can be recovered.
        private string BackupConfigPath => _configPath + ".bak";

        private void LoadConfig()
        {
            bool mainExists = File.Exists(_configPath);
            if (TryReadConfig(_configPath, out var cfg)) { _config = cfg; return; }
            if (!mainExists && !File.Exists(BackupConfigPath)) return; // first run

            // config.json is missing or unreadable. Set the damaged file aside (never silently
            // overwrite it), then fall back to the backup from the last good save.
            string? damagedCopy = null;
            if (mainExists)
            {
                try
                {
                    damagedCopy = Path.Combine(Path.GetDirectoryName(_configPath)!,
                        $"config.damaged-{DateTime.Now:yyyyMMdd-HHmmss}.json");
                    File.Move(_configPath, damagedCopy);
                }
                catch { damagedCopy = null; }
            }

            if (TryReadConfig(BackupConfigPath, out var backup))
            {
                _config = backup;
                if (mainExists)
                    MessageBox.Show("Your OpenFences settings file was damaged, so your fences were restored " +
                                    "from the last backup." +
                                    (damagedCopy != null ? $"\n\nThe damaged file was kept as:\n{damagedCopy}" : ""),
                                    "OpenFences", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            if (mainExists)
                MessageBox.Show("Your OpenFences settings file couldn't be read and no backup was available, " +
                                "so OpenFences is starting with a fresh layout." +
                                (damagedCopy != null ? $"\n\nThe damaged file was kept as:\n{damagedCopy}" : ""),
                                "OpenFences", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        private static bool TryReadConfig(string path, out AppConfig config)
        {
            config = new AppConfig();
            try
            {
                if (!File.Exists(path)) return false;
                var json = File.ReadAllText(path);

                // Back-compat: old format was just a list of FenceModel
                if (json.TrimStart().StartsWith("["))
                {
                    var legacy = JsonSerializer.Deserialize<List<FenceModel>>(json);
                    if (legacy == null) return false;
                    config.Fences = legacy;
                    return true;
                }

                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (cfg == null) return false;
                config = cfg;
                return true;
            }
            catch { return false; }
        }

        // Write to a temp file, flush it to disk, then swap it in. A crash or power loss can
        // never leave a half-written config.json, and the previous version is kept as .bak.
        private void SaveConfig()
        {
            var tmp = _configPath + ".tmp";
            try
            {
                var json = JsonSerializer.SerializeToUtf8Bytes(_config, JsonOpts);
                using (var fs = new FileStream(tmp, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    fs.Write(json, 0, json.Length);
                    fs.Flush(flushToDisk: true);
                }

                if (File.Exists(_configPath))
                    File.Replace(tmp, _configPath, BackupConfigPath, ignoreMetadataErrors: true);
                else
                    File.Move(tmp, _configPath);
            }
            catch (Exception ex)
            {
                (System.Windows.Application.Current as App)?.SafeLog("SaveConfig", ex);
                try { File.Delete(tmp); } catch { /* ignore */ }
            }
        }

        // ========== Fence windows ==========
        private void SpawnFencesFromConfig()
        {
            foreach (var m in _fences.ToList())
            {
                // One broken fence must never stop the app (and every other fence) from starting.
                try
                {
                    var modelRef = m; // explicit capture
                    var win = new FenceWindow(modelRef);
                    HookFenceWindow(win, modelRef);
                    _openWindows.Add(win);
                    win.Show();
                }
                catch (Exception ex)
                {
                    (System.Windows.Application.Current as App)?.SafeLog($"Spawn fence '{m.Name}'", ex);
                }
            }
        }

        private void HookFenceWindow(FenceWindow win, FenceModel model)
        {
            win.FenceRenamed += (_, __) => SaveConfig();
            win.Changed += (_, __) => SaveConfig();
            win.Closed += (_, __) => _openWindows.Remove(win);

            // Delete fence → remove from config (+ optional folder delete)
            win.DeleteRequested += (_, __) =>
            {
                if (model.IsPortal)
                {
                    // A portal points at the user's own folder — NEVER delete it; just unlink.
                    var ok = MessageBox.Show(
                        $"Remove the portal “{model.Name}”?\n\nThis only removes the fence. Your folder is not deleted:\n{model.FolderPath}",
                        "Remove Portal",
                        MessageBoxButton.OKCancel,
                        MessageBoxImage.Question);

                    if (ok != MessageBoxResult.OK) return;

                    _fences.Remove(model);
                    SaveConfig();
                    win.Close();
                    return;
                }

                // The catch-all is where released items go, so it can only be deleted once empty.
                if (IsCatchAll(win) && model.ItemPaths.Count > 0)
                {
                    MessageBox.Show(
                        $"The “{CatchAllFenceName}” fence holds every desktop item that isn't in another fence, " +
                        "so it can't be deleted while it has items.\n\nMove its items into other fences first.",
                        "Delete Fence", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var choice = MessageBox.Show(
                    $"Delete fence “{model.Name}”?\n\n" +
                    $"Nothing is deleted from your desktop — its items move to the “{CatchAllFenceName}” fence.",
                    "Delete Fence",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.OK);

                if (choice != MessageBoxResult.OK) return;

                var released = model.ItemPaths.ToList();
                _fences.Remove(model);
                win.Close();

                // Desktop icons stay hidden while OpenFences runs, so the items need a new home
                // right away or they'd vanish until the next launch.
                if (released.Count > 0)
                    AssignItemsToFence(EnsureRealFence(CatchAllFenceName), released); // saves
                else
                    SaveConfig();
            };
        }

        // ========== Right-drag rectangle → Create fence ==========
        // Desktop icons are hidden and rendered inside fences, so a drawn box just creates a
        // new empty fence at that spot; drag items into it afterward.
        private void CreateFenceFromRect(Rect screenRect)
        {
            var model = new FenceModel
            {
                Name = UniqueFenceName(),
                Left = screenRect.Left,
                Top = screenRect.Top,
                Width = Math.Max(160, screenRect.Width),
                Height = Math.Max(120, screenRect.Height),
                Collapsed = false,
            };

            _fences.Add(model);
            SaveConfig();

            var win = new FenceWindow(model);
            HookFenceWindow(win, model);
            _openWindows.Add(win);
            win.Show();
            win.EnsureBottomZOrder();
        }

        private string UniqueFenceName()
        {
            string baseName = "Fence";
            int suffix = 1;
            string name;
            do { name = $"{baseName} {suffix++}"; }
            while (_fences.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));
            return name;
        }

        // ========== Desktop-item ownership (path-based, rendered) ==========
        internal const string CatchAllFenceName = "Desktop";

        // Convert legacy name-based ownership into path-based ownership so old configs keep
        // their groupings after the render pivot.
        private void MigrateLegacyOwnership()
        {
            bool changed = false;
            foreach (var m in _fences)
            {
                if (m.IsPortal || m.IconNames.Count == 0) continue;
                foreach (var name in m.IconNames)
                {
                    var path = DesktopItems.ResolvePath(name);
                    if (path != null && !m.ItemPaths.Contains(path, StringComparer.OrdinalIgnoreCase))
                        m.ItemPaths.Add(path);
                }
                m.IconNames.Clear(); // fully migrated
                changed = true;
            }
            if (changed) SaveConfig();
        }

        private IEnumerable<FenceWindow> RealFences => _openWindows.Where(w => !w.IsPortal);

        private bool AnyFenceOwns(string path) => RealFences.Any(w => w.OwnsPath(path));

        // Put every desktop item not already owned by a fence into the catch-all "Desktop" fence.
        private void BuildCatchAll()
        {
            var unowned = DesktopItems.Enumerate()
                .Select(e => e.Path)
                .Where(p => !AnyFenceOwns(p))
                .ToList();
            if (unowned.Count == 0 && RealFences.Any()) return;

            EnsureRealFence(CatchAllFenceName, 80, 80).AddItems(unowned);
            SaveConfig();
        }

        // RequestAssignItems callback: give these desktop items to the target fence, removing
        // them from any other fence that held them, and persist.
        private void AssignItemsToFence(FenceWindow target, IReadOnlyList<string> paths)
        {
            foreach (var path in paths)
                foreach (var w in RealFences)
                    if (w != target) w.RemoveItemPath(path);

            target.AddItems(paths);
            SaveConfig();
        }

        private FenceWindow? FindRealFence(string name) =>
            RealFences.FirstOrDefault(w => string.Equals(w.FenceName, name, StringComparison.OrdinalIgnoreCase));

        // Returns the open window for a named real fence, creating it if needed.
        private FenceWindow EnsureRealFence(string name, double left = 80, double top = 80)
        {
            var existing = FindRealFence(name);
            if (existing != null) return existing;

            var model = new FenceModel
            {
                Name = name,
                Left = left,
                Top = top,
                Width = 420,
                Height = 260,
                Collapsed = false
            };
            _fences.Add(model);
            SaveConfig();

            var win = new FenceWindow(model);
            HookFenceWindow(win, model);
            _openWindows.Add(win);
            win.Show();
            return win;
        }

        // ========== Live desktop folder watcher ==========
        private readonly List<FileSystemWatcher> _desktopWatchers = new();

        private void StartDesktopWatcher()
        {
            foreach (var root in DesktopItems.Roots())
            {
                if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) continue;
                try
                {
                    var w = new FileSystemWatcher(root) { IncludeSubdirectories = false, EnableRaisingEvents = true };
                    w.Created += (_, e) => Dispatcher.BeginInvoke(() => OnDesktopFileCreated(e.FullPath));
                    w.Deleted += (_, e) => Dispatcher.BeginInvoke(() => OnDesktopFileRemoved(e.FullPath));
                    w.Renamed += (_, e) => Dispatcher.BeginInvoke(() =>
                    {
                        OnDesktopFileRemoved(e.OldFullPath);
                        OnDesktopFileCreated(e.FullPath);
                    });
                    _desktopWatchers.Add(w);
                }
                catch { /* skip this root */ }
            }
        }

        private void StopDesktopWatcher()
        {
            foreach (var w in _desktopWatchers)
                try { w.EnableRaisingEvents = false; w.Dispose(); } catch { /* ignore */ }
            _desktopWatchers.Clear();
        }

        // A new desktop item appeared → route to a rule target (if auto-organize is on) or the
        // catch-all "Desktop" fence.
        private void OnDesktopFileCreated(string path)
        {
            try
            {
                if (path.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".ini", StringComparison.OrdinalIgnoreCase)) return;
                if (AnyFenceOwns(path)) return;

                string target = CatchAllFenceName;
                if (_config.Options.AutoOrganize)
                {
                    var ruleTarget = DesktopRules.ResolveTargetFence(path, _config.Rules);
                    if (!string.IsNullOrWhiteSpace(ruleTarget)) target = ruleTarget!;
                }

                AssignItemsToFence(EnsureRealFence(target), new[] { path });
            }
            catch { /* ignore one bad item */ }
        }

        // A desktop item was removed/renamed away → drop it from whatever fence owned it.
        private void OnDesktopFileRemoved(string path)
        {
            bool changed = false;
            foreach (var w in RealFences)
                if (w.RemoveItemPath(path)) changed = true;
            if (changed) SaveConfig();
        }

        // ========== Menu / Buttons ==========
        // New fences/portals open on whichever monitor the cursor is on, so they're
        // never lost off-screen on a multi-monitor setup.
        private (double left, double top) SpawnNearCursor()
        {
            try
            {
                var p = WinForms.Cursor.Position; // screen pixels
                var src = PresentationSource.FromVisual(this);
                double sx = src?.CompositionTarget?.TransformFromDevice.M11 ?? 1.0;
                double sy = src?.CompositionTarget?.TransformFromDevice.M22 ?? 1.0;
                // Convert device pixels → WPF DIPs and nudge so the title bar sits under the cursor.
                return (p.X * sx - 40, p.Y * sy - 16);
            }
            catch { return (120, 120); }
        }

        private void NewFence_Click(object? sender, RoutedEventArgs? e)
        {
            var (left, top) = SpawnNearCursor();
            var model = new FenceModel
            {
                Name = UniqueFenceName(),
                Left = left,
                Top = top,
                Width = 420,
                Height = 260,
                Collapsed = false
            };

            _fences.Add(model);
            SaveConfig();

            var win = new FenceWindow(model);
            HookFenceWindow(win, model);
            _openWindows.Add(win);
            win.Show();
        }

        private void NewFolderPortal_Click(object? sender, RoutedEventArgs? e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose a folder to mirror as a Portal"
            };
            if (dlg.ShowDialog() != true) return;

            string folder = dlg.FolderName;
            if (string.IsNullOrWhiteSpace(folder) || !Directory.Exists(folder)) return;

            // Display name from the folder; de-dupe against existing fences.
            string baseName = new DirectoryInfo(folder).Name;
            if (string.IsNullOrWhiteSpace(baseName)) baseName = "Portal";
            string name = baseName;
            int n = 1;
            while (_fences.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
                name = $"{baseName} ({++n})";

            var (left, top) = SpawnNearCursor();
            var model = new FenceModel
            {
                Name = name,
                FolderPath = folder,
                IsPortal = true,
                Left = left,
                Top = top,
                Width = 440,
                Height = 320,
                Collapsed = false
            };

            _fences.Add(model);
            SaveConfig();

            var win = new FenceWindow(model);
            HookFenceWindow(win, model);
            _openWindows.Add(win);
            win.Show();
        }

        private void Exit_Click(object? sender, RoutedEventArgs? e) => Close();

        private void About_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                var about = new AboutDialog();
                about.Owner = System.Windows.Application.Current?.MainWindow;
                about.ShowDialog();
            }
            catch
            {
                MessageBox.Show("OpenFences\nGroup your desktop into movable fences.\n\n" +
                                "GitHub: https://github.com/chrisdfennell/OpenFences",
                                "About OpenFences",
                                MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void ToggleDesktopIcons_Click(object? sender, RoutedEventArgs? e)
        {
            DesktopHelper.ToggleDesktopIcons();
        }

        private void ShowAll_Click(object? sender, RoutedEventArgs? e)
        {
            foreach (var w in _openWindows)
            {
                w.Show();
                w.EnsureBottomZOrder();
            }
        }

        private void HideAll_Click(object? sender, RoutedEventArgs? e)
        {
            foreach (var w in _openWindows) w.Hide();
        }

        private void OpenFencesFolder_Click(object? sender, RoutedEventArgs? e)
        {
            // Real-icon fences have no backing folder anymore; just open the Desktop.
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                Process.Start(new ProcessStartInfo { FileName = desktop, UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        // Organizes the *real* desktop icons into Apps / Documents / System fences by claiming
        // them (no shortcuts created). Each icon is classified by the desktop file behind it.
        private void AutoImportDesktop_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                var entries = DesktopItems.Enumerate();
                if (entries.Count == 0)
                {
                    MessageBox.Show("No desktop items were found to organize.",
                                    "OpenFences", MessageBoxButton.OK, MessageBoxImage.Information);
                    return;
                }

                var groups = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                void AddTo(string fence, string path)
                {
                    if (!groups.TryGetValue(fence, out var list)) groups[fence] = list = new();
                    list.Add(path);
                }

                int apps = 0, docs = 0, sys = 0;
                foreach (var entry in entries)
                {
                    if (entry.IsSpecial) { AddTo("System", entry.Path); sys++; continue; }
                    if (DesktopRules.IsExecutableItem(entry.Path)) { AddTo("Apps", entry.Path); apps++; }
                    else { AddTo("Documents", entry.Path); docs++; }
                }

                var pos = new Dictionary<string, (double l, double t)>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Apps"] = (80, 80),
                    ["Documents"] = (520, 80),
                    ["System"] = (80, 380),
                };
                foreach (var kv in groups)
                {
                    var (l, t) = pos.TryGetValue(kv.Key, out var p) ? p : (80, 80);
                    AssignItemsToFence(EnsureRealFence(kv.Key, l, t), kv.Value);
                }

                SaveConfig();
                MessageBox.Show($"Auto-import complete.\n\nApps: {apps}\nDocuments: {docs}\nSystem: {sys}",
                                "OpenFences", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Auto-import failed:\n" + ex.Message,
                                "OpenFences", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ========== Rules engine: continuous auto-organize ==========
        private void ApplyAutoOrganizeSetting()
        {
            // The live desktop watcher (StartDesktopWatcher) always runs and reads this flag at
            // event time, so there's no separate organizer to start/stop here.
        }

        // ========== Double-click empty desktop: peek fences and/or toggle icons ==========
        private bool _peeked;

        private void ApplyDoubleClickSetting()
        {
            if (_config.Options.DoubleClickDesktopToToggleIcons || _config.Options.DoubleClickPeekFences)
                DesktopDoubleClickMonitor.Start(OnDesktopDoubleClick);
            else
                DesktopDoubleClickMonitor.Stop();
        }

        private void OnDesktopDoubleClick()
        {
            if (_config.Options.DoubleClickPeekFences) TogglePeek();
            if (_config.Options.DoubleClickDesktopToToggleIcons) DesktopHelper.ToggleDesktopIconsRobust();
        }

        private void TogglePeek()
        {
            _peeked = !_peeked;
            foreach (var w in _openWindows)
            {
                if (_peeked) w.Hide();
                else { w.Show(); w.EnsureBottomZOrder(); }
            }
        }

        // ========== Shutdown ==========
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // QoL: always restore the real desktop icons on exit so users aren’t “stuck hidden”.
            // Synchronous (no DispatcherTimer) because we Shutdown() moments later.
            try { DesktopHelper.RestoreDesktopIconsOnExit(); } catch { }

            SaveConfig();

            _updateTimer?.Stop();
            _displayTimer?.Stop();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            StopDesktopWatcher();
            DesktopDoubleClickMonitor.Stop();
            DesktopRightDragFenceSelector.Stop();
            DesktopLeftDragLasso.Stop();

            if (_tray is not null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            _trayMenu?.Dispose();

            System.Windows.Application.Current?.Shutdown();
        }
    }
}
