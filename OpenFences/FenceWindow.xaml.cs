using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Animation;

// Avoid WinForms clash
using MessageBox = System.Windows.MessageBox;
using MediaColor = System.Windows.Media.Color;

namespace OpenFences
{
    public partial class FenceWindow : Window
    {
        private const double CollapsedHeight = 44;
        private const double MinExpandedHeight = 120;

        private readonly FenceModel _model;
        private readonly FileSystemWatcher _watcher;

        // Set true while we drive Height/Width programmatically (collapse animation,
        // initial layout) so SizeChanged doesn't clobber the model's remembered size.
        private bool _suppressGeometrySave;

        public ObservableCollection<FenceItem> ItemsSource { get; } = new();

        public event EventHandler? FenceRenamed;
        public event EventHandler? DeleteRequested;

        private void Scroller_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            // --- Temporarily commented out for testing ---
            
            if (sender is not ScrollViewer sv) return;

            // Wheel down => positive "notches"
            double notches = -e.Delta / 120.0;
            const double stepPerNotch = 96;

            double target = sv.VerticalOffset + (notches * stepPerNotch);

            double max = Math.Max(0, sv.ExtentHeight - sv.ViewportHeight);
            const double eps = 0.75;

            if (target < eps) target = 0;
            else if (max - target < eps) target = max;
            else target = Math.Max(0, Math.Min(target, max));

            sv.ScrollToVerticalOffset(target);
            sv.UpdateLayout();

            e.Handled = true;
            
            // --- Make sure e.Handled is NOT true ---
            // e.Handled = false; // ADD THIS LINE temporarily
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

            if (_model.Collapsed) SetCollapsed(true, animate: false);

            // Keep fences out of the Alt-Tab switcher (must run once the HWND exists).
            SourceInitialized += (_, __) =>
                DesktopHelper.HideFromAltTab(new WindowInteropHelper(this).Handle);

            Loaded += (_, __) => EnsureBottomZOrder();
            Activated += (_, __) => EnsureBottomZOrder();

            LocationChanged += SaveGeometry;
            SizeChanged += (_, __) => SaveGeometry(null, null);

            // Stop watching the backing folder once this fence is gone.
            Closed += (_, __) => { try { _watcher.Dispose(); } catch { /* ignore */ } };
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
            // While we're animating the collapse, or while collapsed, the window
            // Height is the 44px stub — never persist that as the fence's real size.
            if (_suppressGeometrySave) return;

            _model.Left = Left;
            _model.Top = Top;

            if (!_model.Collapsed)
            {
                _model.Width = Width;
                _model.Height = Height;
            }
            else
            {
                // Width can still change while collapsed; height stays remembered.
                _model.Width = Width;
            }
        }

        public void ReloadItems()
        {
            ItemsSource.Clear();

            List<string> files;
            try
            {
                files = Directory.EnumerateFiles(_model.FolderPath)
                                 .Where(p => !p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
                                 .ToList();
            }
            catch
            {
                return; // folder may have been deleted/renamed out from under us
            }

            // Add tiles immediately (no icon yet) so the UI never blocks on shell calls.
            foreach (var path in files)
            {
                ItemsSource.Add(new FenceItem
                {
                    Path = path,
                    DisplayName = Path.GetFileNameWithoutExtension(path)
                });
            }

            // Resolve icons off the UI thread so big fences don't freeze. This MUST run on an
            // STA thread: IconHelper uses apartment-threaded shell COM (IShellLink), which is
            // unreliable on MTA thread-pool threads and silently drops icons. IconHelper freezes
            // the ImageSources, so they're safe to hand back to the UI thread.
            var snapshot = ItemsSource.ToList();
            var loader = new System.Threading.Thread(() =>
            {
                foreach (var item in snapshot)
                {
                    var icon = OpenFences.Services.IconHelper.GetImageSourceForPath(item.Path);
                    if (icon == null) continue;
                    Dispatcher.BeginInvoke(() => item.Icon = icon);
                }
            })
            {
                IsBackground = true,
                Name = "FenceIconLoader"
            };
            loader.SetApartmentState(System.Threading.ApartmentState.STA);
            loader.Start();
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
                SetCollapsed(!_model.Collapsed, animate: true);
            else if (e.LeftButton == MouseButtonState.Pressed)
                DragMove();
        }

        private void SetCollapsed(bool collapsed, bool animate)
        {
            _model.Collapsed = collapsed;

            // Target height: collapsed -> stub; expanded -> remembered height (model.Height
            // is preserved because SaveGeometry skips writes while collapsed/animating).
            double target = collapsed ? CollapsedHeight : Math.Max(_model.Height, MinExpandedHeight);

            if (!animate)
            {
                Scroller.Visibility = collapsed ? Visibility.Collapsed : Visibility.Visible;
                _suppressGeometrySave = true;
                Height = target;
                _suppressGeometrySave = false;
                return;
            }

            if (collapsed)
            {
                // Animate down, then hide the content so it doesn't overflow the stub.
                AnimateHeight(target, onCompleted: () => Scroller.Visibility = Visibility.Collapsed);
            }
            else
            {
                // Show content first, then animate open.
                Scroller.Visibility = Visibility.Visible;
                AnimateHeight(target, onCompleted: null);
            }
        }

        private void AnimateHeight(double to, Action? onCompleted)
        {
            double from = ActualHeight > 0 ? ActualHeight : Height;

            _suppressGeometrySave = true;
            var anim = new DoubleAnimation(from, to, new Duration(TimeSpan.FromMilliseconds(160)))
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseInOut },
                FillBehavior = FillBehavior.Stop
            };
            anim.Completed += (_, __) =>
            {
                BeginAnimation(HeightProperty, null);
                Height = to;                 // commit final value as a local value
                _suppressGeometrySave = false;
                onCompleted?.Invoke();
            };
            BeginAnimation(HeightProperty, anim);
        }

        private void Collapse_Click(object sender, RoutedEventArgs e) => SetCollapsed(!_model.Collapsed, animate: true);

        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        // ---------- Items ----------

        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not FenceItem item) return;

            // Single click selects (highlights) the tile.
            SelectOnly(item);

            // Double click opens it.
            if (e.ClickCount == 2)
            {
                try
                {
                    Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true });
                }
                catch { /* ignore */ }
            }
        }

        private void SelectOnly(FenceItem? item)
        {
            foreach (var i in ItemsSource)
                i.IsSelected = ReferenceEquals(i, item);
        }

        // Clicking empty space inside the fence clears the selection.
        private void Scroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.OriginalSource is DependencyObject d && FindItem(d) is null)
                SelectOnly(null);
        }

        private static FenceItem? FindItem(DependencyObject? start)
        {
            while (start != null)
            {
                if (start is FrameworkElement fe && fe.DataContext is FenceItem item)
                    return item;
                start = VisualTreeHelper.GetParent(start);
            }
            return null;
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
                        // Don't silently swap onto an existing folder — that would orphan
                        // this fence's current shortcuts and surface someone else's.
                        if (Directory.Exists(newFolder))
                        {
                            MessageBox.Show(
                                $"A fence folder named “{newName}” already exists:\n\n{newFolder}\n\n" +
                                "Please choose a different name.",
                                "Name In Use", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }

                        Directory.Move(oldFolder, newFolder);

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
