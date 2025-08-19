using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using MessageBox = System.Windows.MessageBox;

namespace OpenFences
{
    public partial class FenceWindow : Window
    {
        private readonly FenceModel _model;
        private readonly FileSystemWatcher _watcher;
        public ObservableCollection<FenceItem> ItemsSource { get; } = new();

        public event EventHandler? FenceRenamed;

        public FenceWindow(FenceModel model)
        {
            InitializeComponent();
            _model = model;

            TitleText.Text = model.Name;
            Left = model.Left; Top = model.Top;
            Width = model.Width; Height = model.Height;

            AllowDrop = true;
            DragEnter += FenceWindow_DragEnter;
            Drop += FenceWindow_Drop;

            // Ensure fence folder exists
            Directory.CreateDirectory(_model.FolderPath);

            // Load items
            ReloadItems();

            // Watch for changes
            _watcher = new FileSystemWatcher(_model.FolderPath)
            {
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
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

        public void EnsureBottomZOrder()
        {
            DesktopHelper.SendToDesktopLayer(new WindowInteropHelper(this).Handle);
        }

        private void SaveGeometry(object? sender, EventArgs? e)
        {
            _model.Left = Left; _model.Top = Top;
            _model.Width = Width; _model.Height = Height;
        }

        private void ReloadItems()
        {
            ItemsSource.Clear();
            var files = Directory.EnumerateFiles(_model.FolderPath)
                                 .Where(p => !p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

            foreach (var path in files)
            {
                var disp = Path.GetFileNameWithoutExtension(path);
                var icon = IconHelper.GetImageSourceForFile(path);
                ItemsSource.Add(new FenceItem { Path = path, DisplayName = disp, Icon = icon });
            }
        }

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2) SetCollapsed(!_model.Collapsed);
            else DragMove();
        }

        private void SetCollapsed(bool collapsed)
        {
            _model.Collapsed = collapsed;
            Scroller.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
            Height = collapsed ? 44 : Math.Max(_model.Height, 120);
        }

        private void Collapse_Click(object sender, RoutedEventArgs e) => SetCollapsed(!_model.Collapsed);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

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

        // Use WPF DragEventArgs explicitly (no WinForms here)
        private void FenceWindow_DragEnter(object sender, System.Windows.DragEventArgs e)
        {
            e.Effects = (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
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
                    // Create a .lnk shortcut inside fence folder
                    string linkName = Path.Combine(_model.FolderPath, $"{Path.GetFileNameWithoutExtension(p)}.lnk");
                    ShellLink.CreateShortcut(linkName, p);
                }
                catch { /* ignore */ }
            }
            ReloadItems();
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            var prompt = new InputDialog("Rename Fence", "Enter a new name for this fence:", _model.Name);
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
                            // If target exists, just repoint (optionally merge in future)
                        }
                        else
                        {
                            Directory.Move(oldFolder, newFolder);
                        }

                        _model.FolderPath = newFolder;

                        _watcher.EnableRaisingEvents = false;
                        _watcher.Path = newFolder;
                        _watcher.EnableRaisingEvents = true;
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

        private void TitleBar_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Placeholder if you want dynamic enable/disable
        }
    }
}