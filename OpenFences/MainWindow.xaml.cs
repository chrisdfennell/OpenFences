using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

// aliases — keep EXACTLY ONE set in this file
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using MessageBox = System.Windows.MessageBox;

namespace OpenFences
{
    public partial class MainWindow : Window
    {
        // Config lives at %AppData%\OpenFences\config.json
        private readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenFences", "config.json");

        private AppConfig _config = new();
        private List<FenceModel> _fences => _config.Fences;

        private readonly List<FenceWindow> _openWindows = new();

        // Tray
        private WinForms.NotifyIcon? _tray;
        private WinForms.ContextMenuStrip? _trayMenu;

        public MainWindow()
        {
            InitializeComponent();

            // Desktop plumbing (WorkerW / SHELLDLL_DefView)
            DesktopHelper.InitializeDesktopHandles();

            // Config
            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            LoadConfig();

            // Initialize Settings checkboxes
            ChkRunAtStartup.IsChecked = _config.Options.RunAtStartup || StartupHelper.IsRunAtStartupEnabled();
            ChkHideIconsOnStart.IsChecked = _config.Options.HideIconsOnStartup;
            ChkDoubleClickDesktop.IsChecked = _config.Options.DoubleClickDesktopToToggleIcons;

            // Apply behaviors on launch
            if (ChkRunAtStartup.IsChecked == true) StartupHelper.SetRunAtStartup(true);
            if (ChkHideIconsOnStart.IsChecked == true) DesktopHelper.ShowDesktopIcons(false);
            if (ChkDoubleClickDesktop.IsChecked == true) DesktopDoubleClickMonitor.Start();

            // Spawn fences
            SpawnFencesFromConfig();

            // Minimize to tray behavior
            StateChanged += MainWindow_StateChanged;
            InitTrayIcon();
        }

        // ---------- Borderless header interactions ----------
        private void Header_PreviewMouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            var d = e.OriginalSource as DependencyObject;

            while (d != null)
            {
                // Don't drag when clicking buttons/menu items
                if (d is System.Windows.Controls.Primitives.ButtonBase) return;
                if (d is System.Windows.Controls.MenuItem) return;

                d = GetParentSafe(d);
            }

            try { DragMove(); } catch { /* ignore */ }
        }

        private static DependencyObject? GetParentSafe(DependencyObject current)
        {
            if (current is Visual || current is System.Windows.Media.Media3D.Visual3D)
                return VisualTreeHelper.GetParent(current);
            if (current is System.Windows.Documents.TextElement te)
                return te.Parent;
            return LogicalTreeHelper.GetParent(current);
        }

        private void MinimizeButton_Click(object? sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;
        private void CloseButton_Click(object? sender, RoutedEventArgs e) => Close();

        // ---------- Tray ----------
        private void InitTrayIcon()
        {
            _trayMenu = new WinForms.ContextMenuStrip();

            var restore = new WinForms.ToolStripMenuItem("Restore OpenFences", null, (_, __) => RestoreFromTray());
            var newFence = new WinForms.ToolStripMenuItem("New Fence", null, (_, __) => NewFence_Click(null!, null!));
            var showAll = new WinForms.ToolStripMenuItem("Show All Fences", null, (_, __) => ShowAll_Click(null!, null!));
            var hideAll = new WinForms.ToolStripMenuItem("Hide All Fences", null, (_, __) => HideAll_Click(null!, null!));
            var toggle = new WinForms.ToolStripMenuItem("Toggle Desktop Icons", null, (_, __) => ToggleDesktopIcons_Click(null!, null!));
            var exit = new WinForms.ToolStripMenuItem("Exit", null, (_, __) => Close());

            _trayMenu.Items.Add(restore);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add(newFence);
            _trayMenu.Items.Add(showAll);
            _trayMenu.Items.Add(hideAll);
            _trayMenu.Items.Add(toggle);
            _trayMenu.Items.Add(new WinForms.ToolStripSeparator());
            _trayMenu.Items.Add(exit);

            // Load icon from embedded WPF resource (works in single-file publish)
            var uri = new Uri("pack://application:,,,/OpenFences;component/Assets/open-fence.ico");
            var res = System.Windows.Application.GetResourceStream(uri);

            _tray = new WinForms.NotifyIcon
            {
                Text = "OpenFences",
                Icon = (res != null) ? new Drawing.Icon(res.Stream) : Drawing.SystemIcons.Application,
                Visible = true,
                ContextMenuStrip = _trayMenu
            };

            _tray.DoubleClick += (_, __) => RestoreFromTray();
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

        // ---------- Config I/O ----------
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

                var cfg = JsonSerializer.Deserialize<AppConfig>(json);
                if (cfg != null) _config = cfg;
            }
            catch { /* ignore parse errors for now */ }
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch { /* ignore */ }
        }

        private void SpawnFencesFromConfig()
        {
            foreach (var m in _fences.ToList())
            {
                var win = new FenceWindow(m);
                win.FenceRenamed += (_, __) => SaveConfig();
                win.DeleteRequested += (_, alsoDeleteFolder) => DeleteFence(win, m, alsoDeleteFolder);
                win.Closed += (_, __) => _openWindows.Remove(win);
                _openWindows.Add(win);
                win.Show();
            }
        }

        private void SetAllWatchers(bool enabled)
        {
            foreach (var w in _openWindows) w.SetWatcherEnabled(enabled);
        }
        private void RefreshAllFences()
        {
            foreach (var w in _openWindows) w.ReloadItems();
        }

        // ---------- Menu / Buttons ----------
        private void NewFence_Click(object? sender, RoutedEventArgs? e)
        {
            string baseName = "Fence";
            int suffix = 1;
            string name;
            do { name = $"{baseName} {suffix++}"; } while (_fences.Any(f => f.Name.Equals(name, StringComparison.OrdinalIgnoreCase)));

            string fenceFolder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                                              "Fences", name);
            Directory.CreateDirectory(fenceFolder);

            var model = new FenceModel
            {
                Name = name,
                FolderPath = fenceFolder,
                Left = 80,
                Top = 80,
                Width = 420,
                Height = 260,
                Collapsed = false
            };

            _fences.Add(model);
            SaveConfig();

            var win = new FenceWindow(model);
            win.FenceRenamed += (_, __) => SaveConfig();
            win.DeleteRequested += (_, alsoDeleteFolder) => DeleteFence(win, model, alsoDeleteFolder);
            win.Closed += (_, __) => _openWindows.Remove(win);
            _openWindows.Add(win);
            win.Show();
        }

        private void Exit_Click(object? sender, RoutedEventArgs? e) => Close();

        private void About_Click(object? sender, RoutedEventArgs? e)
        {
            try
            {
                var dlg = new AboutDialog { Owner = this };
                dlg.ShowDialog();
            }
            catch
            {
                MessageBox.Show("OpenFences\nCreate movable/resizable desktop fences.\n\n" +
                                "Drag files onto a fence to create shortcuts.\n" +
                                "Config stored in %AppData%\\OpenFences\\config.json",
                                "About", MessageBoxButton.OK, MessageBoxImage.Information);
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
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = root,
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }

        // ---------- Settings checkbox handlers ----------
        private void ChkRunAtStartup_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _config.Options.RunAtStartup = ChkRunAtStartup.IsChecked == true;
            StartupHelper.SetRunAtStartup(_config.Options.RunAtStartup);
            SaveConfig();
        }

        private void ChkHideIconsOnStart_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _config.Options.HideIconsOnStartup = ChkHideIconsOnStart.IsChecked == true;
            SaveConfig();
        }

        private void ChkDoubleClickDesktop_CheckedChanged(object sender, RoutedEventArgs e)
        {
            _config.Options.DoubleClickDesktopToToggleIcons = ChkDoubleClickDesktop.IsChecked == true;
            if (ChkDoubleClickDesktop.IsChecked == true) DesktopDoubleClickMonitor.Start();
            else DesktopDoubleClickMonitor.Stop();
            SaveConfig();
        }

        // ---------- Auto-Import ----------
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
                            if (IsExecutableTarget(target))
                                apps += CopyShortcut(path, appsFence.FolderPath) ? 1 : 0;
                            else
                                docs += CopyShortcut(path, docsFence.FolderPath) ? 1 : 0;
                        }
                        else if (ext is ".exe" or ".url" or ".appref-ms" or ".msi" or ".bat" or ".cmd" or ".ps1")
                        {
                            if (CreateLinkIfMissing(appsFence.FolderPath, Path.GetFileNameWithoutExtension(path), path)) apps++;
                        }
                        else if (isDir || IsDocumentExtension(ext))
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
                MessageBox.Show("Auto-import failed:\n" + ex.Message, "OpenFences", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                SetAllWatchers(true);
            }
        }

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
            win.FenceRenamed += (_, __) => SaveConfig();
            win.DeleteRequested += (_, alsoDeleteFolder) => DeleteFence(win, model, alsoDeleteFolder);
            win.Closed += (_, __) => _openWindows.Remove(win);
            _openWindows.Add(win);
            win.Show();

            return model;
        }

        private static bool IsExecutableTarget(string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return false;
            string ext = Path.GetExtension(targetPath).ToLowerInvariant();
            return ext is ".exe" or ".bat" or ".cmd" or ".ps1" or ".msi" or ".appref-ms";
        }

        private static bool IsDocumentExtension(string ext)
        {
            return new[]
            {
                ".txt",".md",".rtf",".pdf",
                ".doc",".docx",".odt",
                ".xls",".xlsx",".csv",
                ".ppt",".pptx",
                ".png",".jpg",".jpeg",".gif",".bmp",".webp",
                ".json",".xml",".zip",".7z",".rar"
            }.Contains(ext);
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

        // ---------- Delete fence ----------
        private void DeleteFence(FenceWindow win, FenceModel model, bool alsoDeleteFolder)
        {
            try
            {
                win.SetWatcherEnabled(false);
                _openWindows.Remove(win);
                try { win.Close(); } catch { /* ignore */ }

                _fences.Remove(model);
                SaveConfig();

                if (alsoDeleteFolder && Directory.Exists(model.FolderPath))
                {
                    try
                    {
                        // Try recycle bin
                        Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                            model.FolderPath,
                            Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                            Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                    }
                    catch
                    {
                        // Hard delete fallback
                        try { Directory.Delete(model.FolderPath, recursive: true); } catch { /* ignore */ }
                    }
                }
            }
            catch
            {
                MessageBox.Show("Failed to delete the fence.", "OpenFences", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ---------- Shutdown ----------
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            SaveConfig();
            DesktopDoubleClickMonitor.Stop();

            if (_tray is not null)
            {
                _tray.Visible = false;
                _tray.Dispose();
                _tray = null;
            }
            _trayMenu?.Dispose();

            System.Windows.Application.Current.Shutdown();
        }
    }
}