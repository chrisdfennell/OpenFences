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

            // Spawn fence windows from config
            SpawnFencesFromConfig();

            // Tray + minimize-to-tray behavior
            StateChanged += MainWindow_StateChanged;
            InitTrayIcon();

            // Right-click-drag rectangle to create fence (fast XOR + menu)
            DesktopRightDragFenceSelector.Start(CreateFenceFromRect);

            // Delete/Enter operate on the whole selection across all fences
            FenceWindow.RequestDeleteSelected = DeleteAllSelected;
            FenceWindow.RequestOpenSelected = OpenAllSelected;

            // Left-drag on the empty desktop = lasso that selects items across fences
            DesktopLeftDragLasso.Start(OnLassoUpdate, OnLassoEnd);
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

            bool anyPortal = _openWindows.Any(w => w.IsPortal && w.SelectedCount > 0);
            string msg = total == 1 ? "Delete the selected item?" : $"Delete {total} selected items?";
            if (anyPortal) msg += "\n\nSome are in folder portals — this removes the real files/folders.";

            if (MessageBox.Show(msg, "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            foreach (var w in _openWindows.ToList()) w.DeleteSelectedItemsNoConfirm();
        }

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
            var exit = new WinForms.ToolStripMenuItem("Exit", null, (_, __) => Close());

            _trayMenu.Items.Add(restore);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add(newFence);
            _trayMenu.Items.Add(newPortal);
            _trayMenu.Items.Add(showAll);
            _trayMenu.Items.Add(hideAll);
            _trayMenu.Items.Add(toggle);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
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
                if (s != null) return new Drawing.Icon(s);
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

        private void RestoreFromTray()
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

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(_configPath)) return;
                var json = File.ReadAllText(_configPath);

                // Back-compat: old format was just a list of FenceModel
                if (json.TrimStart().StartsWith("["))
                {
                    var legacy = JsonSerializer.Deserialize<List<FenceModel>>(json);
                    if (legacy != null) _config.Fences = legacy;
                    return;
                }

                var cfg = JsonSerializer.Deserialize<AppConfig>(json, JsonOpts);
                if (cfg != null) _config = cfg;
            }
            catch { /* ignore parse errors for now */ }
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, JsonOpts);
                File.WriteAllText(_configPath, json);
            }
            catch { /* ignore */ }
        }

        // ========== Fence windows ==========
        private void SpawnFencesFromConfig()
        {
            foreach (var m in _fences.ToList())
            {
                var modelRef = m; // explicit capture
                var win = new FenceWindow(modelRef);
                HookFenceWindow(win, modelRef);
                _openWindows.Add(win);
                win.Show();
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

                var choice = MessageBox.Show(
                    $"Delete fence “{model.Name}”?\n\nBacked folder:\n{model.FolderPath}\n\n" +
                    "Click Yes to also delete the folder (and its shortcuts), No to keep the folder, or Cancel.",
                    "Delete Fence",
                    MessageBoxButton.YesNoCancel,
                    MessageBoxImage.Warning,
                    MessageBoxResult.No);

                if (choice == MessageBoxResult.Cancel) return;

                _fences.Remove(model);
                SaveConfig();

                try
                {
                    if (choice == MessageBoxResult.Yes && Directory.Exists(model.FolderPath))
                        Directory.Delete(model.FolderPath, true);
                }
                catch { /* ignore filesystem errors */ }

                win.Close();
            };
        }

        private void SetAllWatchers(bool enabled)
        {
            foreach (var w in _openWindows) w.SetWatcherEnabled(enabled);
        }

        private void RefreshAllFences()
        {
            foreach (var w in _openWindows) w.ReloadItems();
        }

        // ========== Right-drag rectangle → Create fence ==========
        private void CreateFenceFromRect(Rect screenRect)
        {
            // Unique name
            string baseName = "Fence";
            int suffix = 1;
            string name;
            do { name = $"{baseName} {suffix++}"; }
            while (_fences.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

            // Backing folder
            string fenceFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Fences", name);
            Directory.CreateDirectory(fenceFolder);

            // Build model
            var model = new FenceModel
            {
                Name = name,
                FolderPath = fenceFolder,
                Left = screenRect.Left,
                Top = screenRect.Top,
                Width = Math.Max(120, screenRect.Width),
                Height = Math.Max(100, screenRect.Height),
                Collapsed = false
            };

            _fences.Add(model);
            SaveConfig();

            var win = new FenceWindow(model);
            HookFenceWindow(win, model);
            _openWindows.Add(win);
            win.Show();
            win.EnsureBottomZOrder();
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
            string baseName = "Fence";
            int suffix = 1;
            string name;
            do { name = $"{baseName} {suffix++}"; } while (_fences.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

            string fenceFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                              "Fences", name);
            Directory.CreateDirectory(fenceFolder);

            var (left, top) = SpawnNearCursor();
            var model = new FenceModel
            {
                Name = name,
                FolderPath = fenceFolder,
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
            foreach (var w in _openWindows) { w.Show(); w.EnsureBottomZOrder(); }
        }

        private void HideAll_Click(object? sender, RoutedEventArgs? e)
        {
            foreach (var w in _openWindows) w.Hide();
        }

        private void OpenFencesFolder_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                var root = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Fences");
                if (!Directory.Exists(root)) Directory.CreateDirectory(root);
                Process.Start(new ProcessStartInfo
                {
                    FileName = root,
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }

        private void AutoImportDesktop_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                SetAllWatchers(false);

                var appsFence = EnsureFence("Apps", left: 80, top: 80);
                var docsFence = EnsureFence("Documents", left: 520, top: 80);
                var systemFence = EnsureFence("System", left: 80, top: 380);

                int apps = 0, docs = 0;

                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fencesRoot = Path.Combine(desktop, "Fences");
                var items = Directory.EnumerateFileSystemEntries(desktop)
                                     .Where(p => !string.Equals(p, fencesRoot, StringComparison.OrdinalIgnoreCase));

                foreach (var path in items)
                {
                    try
                    {
                        bool isDir = Directory.Exists(path);
                        string ext = Path.GetExtension(path).ToLowerInvariant();

                        if (ext == ".lnk")
                        {
                            var target = ShellLink.GetShortcutTarget(path);
                            if (DesktopRules.IsExecutableTarget(target))
                                apps += CopyShortcut(path, appsFence.FolderPath) ? 1 : 0;
                            else
                                docs += CopyShortcut(path, docsFence.FolderPath) ? 1 : 0;
                        }
                        else if (ext is ".exe" or ".url" or ".appref-ms" or ".msi" or ".bat" or ".cmd" or ".ps1")
                        {
                            if (CreateLinkIfMissing(appsFence.FolderPath, Path.GetFileNameWithoutExtension(path), path)) apps++;
                        }
                        else if (isDir || DesktopRules.IsDocumentExtension(ext))
                        {
                            if (CreateLinkIfMissing(docsFence.FolderPath, Path.GetFileName(path), path)) docs++;
                        }
                        else
                        {
                            if (CreateLinkIfMissing(docsFence.FolderPath, Path.GetFileNameWithoutExtension(path), path)) docs++;
                        }
                    }
                    catch { /* skip single item */ }
                }

                // System fence: CLSIDs + Home
                int sys = SystemShortcuts.AddToFolder(systemFence.FolderPath);

                SaveConfig();
                RefreshAllFences();

                MessageBox.Show($"Auto-import complete.\n\nApps: {apps}\nDocuments: {docs}\nSystem: {sys}",
                                "OpenFences", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Auto-import failed:\n" + ex.Message,
                                "OpenFences", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetAllWatchers(true);
            }
        }

        // ========== Helpers ==========
        private FenceModel EnsureFence(string name, double left, double top)
        {
            var existing = _fences.FirstOrDefault(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
            if (existing != null) return existing;

            string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "Fences", name);
            Directory.CreateDirectory(folder);

            var model = new FenceModel
            {
                Name = name,
                FolderPath = folder,
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

            return model;
        }

        // ========== Rules engine: continuous auto-organize ==========
        private void ApplyAutoOrganizeSetting()
        {
            if (_config.Options.AutoOrganize)
                DesktopOrganizer.Start(OnDesktopItemAdded);
            else
                DesktopOrganizer.Stop();
        }

        // Called on the UI (STA) thread for each newly added desktop item.
        private void OnDesktopItemAdded(string path)
        {
            try
            {
                var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                var fencesRoot = Path.Combine(desktop, "Fences");

                // Never re-process our own fence shortcuts.
                if (path.StartsWith(fencesRoot, StringComparison.OrdinalIgnoreCase)) return;

                var targetName = DesktopRules.ResolveTargetFence(path, _config.Rules);
                if (string.IsNullOrWhiteSpace(targetName)) return;

                var fence = EnsureFence(targetName!, left: 80, top: 80);

                string ext = Path.GetExtension(path).ToLowerInvariant();
                if (ext == ".lnk")
                    CopyShortcut(path, fence.FolderPath);              // keep existing shortcut
                else
                    CreateLinkIfMissing(fence.FolderPath,
                        Directory.Exists(path) ? Path.GetFileName(path) : Path.GetFileNameWithoutExtension(path),
                        path);
            }
            catch { /* ignore one bad item */ }
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

        private static bool CopyShortcut(string sourceLnk, string destFolder)
        {
            try
            {
                string dest = Path.Combine(destFolder, Path.GetFileName(sourceLnk));
                if (File.Exists(dest)) return false;
                File.Copy(sourceLnk, dest);
                return true;
            }
            catch { return false; }
        }

        private static bool CreateLinkIfMissing(string destFolder, string displayName, string targetPath)
        {
            try
            {
                string link = Path.Combine(destFolder, SanitizeFileName(displayName) + ".lnk");
                if (File.Exists(link)) return false;
                ShellLink.CreateShortcut(link, targetPath);
                return true;
            }
            catch { return false; }
        }

        private static string SanitizeFileName(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            return name.Trim();
        }

        // ========== Shutdown ==========
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            // QoL: always restore icons on exit so users aren’t “stuck hidden”.
            // Synchronous (no DispatcherTimer) because we Shutdown() moments later.
            try { DesktopHelper.RestoreDesktopIconsOnExit(); } catch { }

            SaveConfig();

            DesktopDoubleClickMonitor.Stop();
            DesktopRightDragFenceSelector.Stop();
            DesktopLeftDragLasso.Stop();
            DesktopOrganizer.Stop();

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
