using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Input;

// Avoid WinForms clash
using MessageBox = System.Windows.MessageBox;
using MediaColor = System.Windows.Media.Color;

namespace OpenFences
{
    public partial class FenceWindow : Window
    {
        private readonly FenceModel _model;
        private readonly FileSystemWatcher _watcher;

        public ObservableCollection<FenceItem> ItemsSource { get; } = new();

        public event EventHandler? FenceRenamed;
        public event EventHandler? DeleteRequested;

        private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (sender is not ScrollViewer sv) return;

            // Wheel down => positive "notches" (scroll content down)
            double notches = -e.Delta / 120.0;
            const double stepPerNotch = 96; // ~a row or two; tweak to taste

            double target = sv.VerticalOffset - (notches * stepPerNotch);

            // Clamp with a tiny epsilon to defeat rounding "dead bands"
            double max = Math.Max(0, sv.ExtentHeight - sv.ViewportHeight);
            const double eps = 0.75;

            if (target < eps) target = 0;
            else if (max - target < eps) target = max;
            else target = Math.Max(0, Math.Min(target, max));

            sv.ScrollToVerticalOffset(target);
            e.Handled = true;
        }


        public FenceWindow(FenceModel model)
        {
            InitializeComponent();
            _model = model;

            TitleText.Text = model.Name;
            Left = model.Left; Top = model.Top;
            Width = model.Width; Height = model.Height;

            ApplyBackground();

            // DnD for dropping files to create shortcuts
            AllowDrop = true;
            DragEnter += FenceWindow_DragEnter;
            Drop += FenceWindow_Drop;

            // Ensure backing folder exists
            Directory.CreateDirectory(_model.FolderPath);

            // Load initial items
            ReloadItems();

            // Watch for folder changes
            _watcher = new FileSystemWatcher(_model.FolderPath)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, __) => Dispatcher.Invoke(ReloadItems);
            _watcher.Deleted += (_, __) => Dispatcher.Invoke(ReloadItems);
            _watcher.Renamed += (_, __) => Dispatcher.Invoke(ReloadItems);

            Items.ItemsSource = ItemsSource;

            if (_model.Collapsed) SetCollapsed(true);

            Loaded += (_, __) => EnsureBottomZOrder();
            Activated += (_, __) => EnsureBottomZOrder();

            LocationChanged += SaveGeometry;
            SizeChanged += (_, __) => SaveGeometry(null, null);
        }

        // ---------- UI/Background ----------

        private void ApplyBackground()
        {
            var baseColor = MediaColor.FromRgb(0x20, 0x20, 0x20); // #202020
            byte a = (byte)Math.Round(255 * Math.Clamp(_model.BackgroundOpacity, 0.0, 1.0));
            RootBorder.Background = new SolidColorBrush(MediaColor.FromArgb(a, baseColor.R, baseColor.G, baseColor.B));
        }

        public void EnsureBottomZOrder()
        {
            DesktopHelper.SendToDesktopLayer(new WindowInteropHelper(this).Handle);
        }

        private void SaveGeometry(object? sender, EventArgs? e)
        {
            _model.Left = Left;
            _model.Top = Top;
            _model.Width = Width;
            _model.Height = Height;
        }

        public void ReloadItems()
        {
            ItemsSource.Clear();
            var files = Directory.EnumerateFiles(_model.FolderPath)
                                 .Where(p => !p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));
            foreach (var path in files)
            {
                var disp = Path.GetFileNameWithoutExtension(path);
                var icon = OpenFences.Services.IconHelper.GetImageSourceForPath(path);
                ItemsSource.Add(new FenceItem { Path = path, DisplayName = disp, Icon = icon });
            }
        }
        // ---------- Context menu ----------
        private FenceItem? MenuSenderToItem(object sender)
        {
            if (sender is FrameworkElement fe)
                return fe.DataContext as FenceItem;
            return null;
        }

        // ---------- Context menu actions ----------
        private void Item_Open_Click(object sender, RoutedEventArgs e)
        {
            if (MenuSenderToItem(sender) is not FenceItem item) return;
            try
            {
                var psi = new ProcessStartInfo(item.Path) { UseShellExecute = true };
                Process.Start(psi);
            }
            catch { /* ignore */ }
        }

        private void Item_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (MenuSenderToItem(sender) is not FenceItem item) return;
            try
            {
                var dir = Path.GetDirectoryName(item.Path);
                if (!string.IsNullOrEmpty(dir))
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        private void Item_Delete_Click(object sender, RoutedEventArgs e)
        {
            if (MenuSenderToItem(sender) is not FenceItem item) return;

            var confirm = MessageBox.Show(
                $"Delete this shortcut?\n\n{item.DisplayName}.lnk",
                "Delete Shortcut",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            try
            {
                File.Delete(item.Path); // delete only the .lnk in the fence folder
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not delete shortcut:\n" + ex.Message,
                                "OpenFences", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // Remove from UI list
            ItemsSource.Remove(item);
        }

        public void SetWatcherEnabled(bool enabled)
        {
            try { _watcher.EnableRaisingEvents = enabled; } catch { /* ignore */ }
        }

        // ---------- Title bar ----------

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                SetCollapsed(!_model.Collapsed);
            else if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void SetCollapsed(bool collapsed)
        {
            _model.Collapsed = collapsed;
            Scroller.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            Height = collapsed ? 44 : Math.Max(_model.Height, 120);
        }

        private void Collapse_Click(object sender, RoutedEventArgs e) => SetCollapsed(!_model.Collapsed);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ---------- Items ----------

        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && sender is FrameworkElement fe && fe.DataContext is FenceItem item)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                }
                catch { /* ignore */ }
            }
        }

        // WPF DragEventArgs explicitly (avoid WinForms ambiguity)
        private void FenceWindow_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)
                ? System.Windows.DragDropEffects.Copy
                : System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void FenceWindow_Drop(object sender, System.Windows.DragEventArgs e)
        {
            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;

            foreach (var p in paths)
            {
                try
                {
                    string linkName = Path.Combine(_model.FolderPath, $"{Path.GetFileNameWithoutExtension(p)}.lnk");
                    ShellLink.CreateShortcut(linkName, p);
                }
                catch { /* ignore */ }
            }

            ReloadItems();
        }

        // ---------- Context menu actions ----------

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            var prompt = new InputDialog("Rename Fence", "Enter a new name for this fence:", _model.Name)
            {
                Owner = this
            };
            if (prompt.ShowDialog() == true)
            {
                var newName = prompt.Value.Trim();
                if (string.IsNullOrWhiteSpace(newName) ||
                    string.Equals(newName, _model.Name, StringComparison.OrdinalIgnoreCase))
                    return;

                var invalid = Path.GetInvalidFileNameChars();
                if (newName.IndexOfAny(invalid) >= 0)
                {
                    MessageBox.Show("That name contains invalid characters for a folder. Please choose a different name.",
                                    "Invalid Name", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                try
                {
                    var fencesRoot = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                        "Fences");

                    var oldFolder = _model.FolderPath;
                    var newFolder = Path.Combine(fencesRoot, newName);

                    Directory.CreateDirectory(fencesRoot);

                    if (!string.Equals(oldFolder, newFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        if (Directory.Exists(newFolder))
                        {
                            // target exists -> just repoint
                        }
                        else
                        {
                            Directory.Move(oldFolder, newFolder);
                        }

                        _watcher.EnableRaisingEvents = false;
                        _watcher.Path = newFolder;
                        _watcher.EnableRaisingEvents = true;

                        _model.FolderPath = newFolder;
                    }

                    _model.Name = newName;
                    TitleText.Text = _model.Name;

                    ReloadItems();
                    FenceRenamed?.Invoke(this, EventArgs.Empty);
                }
                catch (Exception ex)
                {
                    MessageBox.Show("Rename failed:\n" + ex.Message, "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = _model.FolderPath,
                    UseShellExecute = true
                });
            }
            catch { /* ignore */ }
        }

        private void AddSystemShortcuts_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SetWatcherEnabled(false);
                int count = SystemShortcuts.AddToFolder(_model.FolderPath);
                ReloadItems();
                MessageBox.Show($"Added {count} system shortcut(s).",
                    "OpenFences", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            finally
            {
                SetWatcherEnabled(true);
            }
        }

        private void Transparency_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && double.TryParse(Convert.ToString(mi.Tag), out double alpha))
            {
                _model.BackgroundOpacity = Math.Clamp(alpha, 0.0, 1.0);
                ApplyBackground();
            }
        }

        private void TitleBar_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Placeholder for dynamic enable/disable if needed later
        }

        private void DeleteFence_Click(object sender, RoutedEventArgs e)
        {
            var result = MessageBox.Show(
                "Delete this fence?\n\nChoose Yes to also delete its backing folder and shortcuts from disk.\nChoose No to remove the fence but keep the folder.\nCancel to abort.",
                "Delete Fence",
                MessageBoxButton.YesNoCancel,
                MessageBoxImage.Warning,
                MessageBoxResult.No);

            if (result == MessageBoxResult.Cancel) return;

            // We signal deletion; MainWindow handles tearing down + optional folder delete.
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
