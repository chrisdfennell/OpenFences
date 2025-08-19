using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;

// alias WinForms types for tray
using WinForms = System.Windows.Forms;
using Drawing = System.Drawing;
using MessageBox = System.Windows.MessageBox;

namespace OpenFences
{
    public partial class MainWindow : Window
    {
        private readonly string _configPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenFences", "config.json");

        private readonly List<FenceModel> _fences = new();
        private readonly List<FenceWindow> _openWindows = new();

        // Tray
        private WinForms.NotifyIcon? _tray;
        private WinForms.ContextMenuStrip? _trayMenu;

        public MainWindow()
        {
            InitializeComponent();

            Directory.CreateDirectory(Path.GetDirectoryName(_configPath)!);
            LoadConfig();
            SpawnFencesFromConfig();

            // minimize to tray
            StateChanged += MainWindow_StateChanged;
            InitTrayIcon();
        }

        // ---------- Borderless window header ----------
        private void Header_MouseLeftButtonDown(object? sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left && e.ButtonState == MouseButtonState.Pressed)
                DragMove();
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

            _tray = new WinForms.NotifyIcon
            {
                Text = "OpenFences",
                Icon = Drawing.SystemIcons.Application, // replace with your .ico if desired
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

        // ---------- Config ----------
        private void LoadConfig()
        {
            if (!File.Exists(_configPath)) return;
            try
            {
                var json = File.ReadAllText(_configPath);
                var list = JsonSerializer.Deserialize<List<FenceModel>>(json);
                if (list != null) _fences.AddRange(list);
            }
            catch { /* ignore parse errors for now */ }
        }

        private void SaveConfig()
        {
            try
            {
                var json = JsonSerializer.Serialize(_fences, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch { /* ignore */ }
        }

        private void SpawnFencesFromConfig()
        {
            foreach (var m in _fences)
            {
                var win = new FenceWindow(m);
                win.FenceRenamed += (_, __) => SaveConfig();
                win.Closed += (_, __) => _openWindows.Remove(win);
                _openWindows.Add(win);
                win.Show();
            }
        }

        // ---------- Helpers for bulk ops ----------
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
            win.Closed += (_, __) => _openWindows.Remove(win);
            _openWindows.Add(win);
            win.Show();
        }

        private void Exit_Click(object? sender, RoutedEventArgs? e) => Close();

        private void About_Click(object? sender, RoutedEventArgs? e)
        {
            MessageBox.Show("OpenFences\nCreate movable/resizable desktop fences.\n\n" +
                            "Drag files onto a fence to create shortcuts.\n" +
                            "Config stored in %AppData%\\OpenFences\\config.json",
                            "About", MessageBoxButton.OK, MessageBoxImage.Information);
        }

        private void ToggleDesktopIcons_Click(object? sender, RoutedEventArgs? e) => DesktopHelper.ToggleDesktopIcons();

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

        // ---------- Shutdown ----------
        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);

            SaveConfig();

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
