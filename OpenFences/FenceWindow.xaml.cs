using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
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
using Point = System.Windows.Point;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;

namespace OpenFences
{
    public partial class FenceWindow : Window, INotifyPropertyChanged
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
        // Raised whenever a persisted setting changes (sort, icon size, transparency).
        public event EventHandler? Changed;

        // ----- Layout metrics bound by the XAML (per-fence icon size) -----
        public int IconPx => (_model?.IconSize ?? FenceIconSize.Medium) switch
        {
            FenceIconSize.Small => 32,
            FenceIconSize.Large => 64,
            _ => 48
        };
        public double TileWidth => IconPx + 44;
        public double TileHeight => IconPx + 50;
        public double TileContentWidth => TileWidth - 8;

        public event PropertyChangedEventHandler? PropertyChanged;
        private void OnPropertyChanged([CallerMemberName] string? name = null)
            => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private void RaiseLayoutMetricsChanged()
        {
            OnPropertyChanged(nameof(IconPx));
            OnPropertyChanged(nameof(TileWidth));
            OnPropertyChanged(nameof(TileHeight));
            OnPropertyChanged(nameof(TileContentWidth));
        }


        public FenceWindow(FenceModel model)
        {
            InitializeComponent();
            _model = model;

            TitleText.Text = model.IsPortal ? "🗁 " + model.Name : model.Name;
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

            List<string> entries = new();
            try
            {
                // A portal mirrors a real folder: show its subfolders and files.
                // A normal fence only holds its own .lnk shortcuts (files).
                if (_model.IsPortal)
                    entries.AddRange(Directory.EnumerateDirectories(_model.FolderPath));

                entries.AddRange(Directory.EnumerateFiles(_model.FolderPath)
                                          .Where(p => !p.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase)));
            }
            catch
            {
                return; // folder may have been deleted/renamed out from under us
            }

            entries = SortEntries(entries);

            // Add tiles immediately (no icon yet) so the UI never blocks on shell calls.
            foreach (var path in entries)
            {
                ItemsSource.Add(new FenceItem
                {
                    Path = path,
                    DisplayName = Directory.Exists(path)
                        ? Path.GetFileName(path)
                        : Path.GetFileNameWithoutExtension(path)
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

        private List<string> SortEntries(List<string> entries)
        {
            // Portals list folders before files; normal fences have no folders.
            bool dirsFirst = _model.IsPortal;
            var seeded = dirsFirst
                ? entries.OrderBy(p => Directory.Exists(p) ? 0 : 1)
                : entries.OrderBy(_ => 0);

            var oic = StringComparer.OrdinalIgnoreCase;

            IOrderedEnumerable<string> ordered = _model.Sort switch
            {
                FenceSort.Manual => seeded,
                FenceSort.Type => seeded.ThenBy(p => Path.GetExtension(p), oic)
                                        .ThenBy(p => Path.GetFileName(p), oic),
                FenceSort.DateModified => seeded.ThenByDescending(SafeWriteTime),
                FenceSort.Size => seeded.ThenByDescending(SafeSize),
                _ => seeded.ThenBy(p => Path.GetFileName(p), oic) // Name
            };

            return ordered.ToList();
        }

        private static DateTime SafeWriteTime(string p)
        {
            try { return File.GetLastWriteTimeUtc(p); } catch { return DateTime.MinValue; }
        }

        private static long SafeSize(string p)
        {
            try { return Directory.Exists(p) ? 0 : new FileInfo(p).Length; } catch { return 0; }
        }

        // ---------- Sort / icon-size menu actions ----------
        private void Sort_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && Enum.TryParse<FenceSort>(Convert.ToString(mi.Tag), out var mode))
            {
                _model.Sort = mode;
                ReloadItems();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void IconSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && Enum.TryParse<FenceIconSize>(Convert.ToString(mi.Tag), out var size))
            {
                _model.IconSize = size;
                RaiseLayoutMetricsChanged();
                Changed?.Invoke(this, EventArgs.Empty);
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
            // Right-click already selected the item; delete the whole selection together.
            if (MenuSenderToItem(sender) is FenceItem item && !item.IsSelected) SelectOnly(item);
            (RequestDeleteSelected ?? DeleteOwnSelectedWithConfirm)();
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
            Scroller.Focus();

            if (e.ClickCount == 2) { OpenItem(item); return; }

            // Ctrl+click toggles; plain click selects just this one.
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                item.IsSelected = !item.IsSelected;
            else
                SelectOnly(item);
        }

        // Right-clicking an unselected item selects just it (so the menu acts on it).
        private void Item_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FenceItem item && !item.IsSelected)
                SelectOnly(item);
        }

        private void SelectOnly(FenceItem? item)
        {
            foreach (var i in ItemsSource)
                i.IsSelected = ReferenceEquals(i, item);
        }

        private static void OpenItem(FenceItem item)
        {
            try { Process.Start(new ProcessStartInfo(item.Path) { UseShellExecute = true }); }
            catch { /* ignore */ }
        }

        // MainWindow wires these so Delete/Enter act on the WHOLE selection across all
        // fences (e.g. after a desktop lasso). Fallback to this fence if not wired.
        public static Action? RequestDeleteSelected;
        public static Action? RequestOpenSelected;

        public bool IsPortal => _model.IsPortal;
        public int SelectedCount => ItemsSource.Count(i => i.IsSelected);
        public void ClearSelection() => SelectOnly(null);

        public void OpenSelectedItems()
        {
            foreach (var item in ItemsSource.Where(i => i.IsSelected).ToList()) OpenItem(item);
        }

        // Deletes this fence's selected items without prompting (caller confirms).
        public void DeleteSelectedItemsNoConfirm()
        {
            var selected = ItemsSource.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return;

            SetWatcherEnabled(false);
            try
            {
                foreach (var item in selected)
                {
                    try
                    {
                        if (Directory.Exists(item.Path)) Directory.Delete(item.Path, true);
                        else File.Delete(item.Path);
                        ItemsSource.Remove(item);
                    }
                    catch { /* skip one */ }
                }
            }
            finally { SetWatcherEnabled(true); }
        }

        // Selects items whose on-screen bounds fall inside a physical-pixel rect
        // (used by the cross-fence desktop lasso).
        public void SelectItemsInScreenRectPx(Rect lassoPx, bool additive)
        {
            for (int i = 0; i < ItemsSource.Count; i++)
            {
                if (Items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                Rect itemPx;
                try
                {
                    var tl = c.PointToScreen(new Point(0, 0));
                    var br = c.PointToScreen(new Point(c.ActualWidth, c.ActualHeight));
                    itemPx = new Rect(tl, br);
                }
                catch { continue; }

                if (lassoPx.IntersectsWith(itemPx)) ItemsSource[i].IsSelected = true;
                else if (!additive) ItemsSource[i].IsSelected = false;
            }
        }

        // Fallback delete (single fence) used only if MainWindow hasn't wired the global one.
        private void DeleteOwnSelectedWithConfirm()
        {
            int n = SelectedCount;
            if (n == 0) return;
            string what = n == 1 ? "the selected item" : $"{n} selected items";
            string msg = _model.IsPortal
                ? $"Delete {what} from the folder?\n\nThis removes the real file(s)/folder(s)."
                : $"Delete {what}?";
            if (MessageBox.Show(msg, "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;
            DeleteSelectedItemsNoConfirm();
        }

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete) { (RequestDeleteSelected ?? DeleteOwnSelectedWithConfirm)(); e.Handled = true; }
            else if (e.Key == Key.Enter) { (RequestOpenSelected ?? OpenSelectedItems)(); e.Handled = true; }
            else if (e.Key == Key.A && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
            {
                foreach (var i in ItemsSource) i.IsSelected = true;
                e.Handled = true;
            }
        }

        // ---------- Marquee (rubber-band) selection ----------
        private bool _marqueeActive;
        private bool _marqueeAdditive;
        private bool _marqueeMoved;
        private Point _marqueeStart;

        private void Scroller_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Clicks that land on an item are handled by the item itself.
            if (FindItem(e.OriginalSource as DependencyObject) != null) return;

            Scroller.Focus();
            _marqueeAdditive = (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control;
            if (!_marqueeAdditive) SelectOnly(null);

            _marqueeActive = true;
            _marqueeMoved = false;
            _marqueeStart = e.GetPosition(MarqueeLayer);
            UpdateMarquee(_marqueeStart);
            MarqueeRect.Visibility = Visibility.Visible;
            Scroller.CaptureMouse();
        }

        private void Scroller_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            if (!_marqueeActive) return;
            if (e.LeftButton != MouseButtonState.Pressed) { EndMarquee(); return; }

            var cur = e.GetPosition(MarqueeLayer);
            if (!_marqueeMoved &&
                (Math.Abs(cur.X - _marqueeStart.X) > 3 || Math.Abs(cur.Y - _marqueeStart.Y) > 3))
                _marqueeMoved = true;

            UpdateMarquee(cur);
            SelectWithinMarquee();
        }

        private void Scroller_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e) => EndMarquee();

        private void EndMarquee()
        {
            if (!_marqueeActive) return;
            _marqueeActive = false;
            if (Scroller.IsMouseCaptured) Scroller.ReleaseMouseCapture();
            MarqueeRect.Visibility = Visibility.Collapsed;
        }

        private void UpdateMarquee(Point cur)
        {
            double x = Math.Min(cur.X, _marqueeStart.X);
            double y = Math.Min(cur.Y, _marqueeStart.Y);
            Canvas.SetLeft(MarqueeRect, x);
            Canvas.SetTop(MarqueeRect, y);
            MarqueeRect.Width = Math.Abs(cur.X - _marqueeStart.X);
            MarqueeRect.Height = Math.Abs(cur.Y - _marqueeStart.Y);
        }

        private void SelectWithinMarquee()
        {
            var box = new Rect(Canvas.GetLeft(MarqueeRect), Canvas.GetTop(MarqueeRect),
                               MarqueeRect.Width, MarqueeRect.Height);

            for (int i = 0; i < ItemsSource.Count; i++)
            {
                if (Items.ItemContainerGenerator.ContainerFromIndex(i) is not FrameworkElement c) continue;
                Rect b;
                try { b = c.TransformToVisual(MarqueeLayer).TransformBounds(new Rect(0, 0, c.ActualWidth, c.ActualHeight)); }
                catch { continue; }

                if (box.IntersectsWith(b)) ItemsSource[i].IsSelected = true;
                else if (!_marqueeAdditive) ItemsSource[i].IsSelected = false;
            }
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
                    if (_model.IsPortal)
                    {
                        // A portal is a live folder view: copy the real item into it.
                        if (Directory.Exists(p)) continue; // skip dropped folders for safety
                        var dest = Path.Combine(_model.FolderPath, Path.GetFileName(p));
                        if (!File.Exists(dest)) File.Copy(p, dest);
                    }
                    else
                    {
                        string linkName = Path.Combine(_model.FolderPath, $"{Path.GetFileNameWithoutExtension(p)}.lnk");
                        ShellLink.CreateShortcut(linkName, p);
                    }
                }
                catch { /* ignore */ }
            }

            ReloadItems();
        }

        // ---------- Context menu actions ----------

        // The ✎ button opens the full management menu (rename, sort, icon size,
        // transparency, delete) so everything is reachable without right-clicking.
        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || FenceMenu is null) return;
            CheckByTag(SortMenu, _model.Sort.ToString());
            CheckByTag(IconSizeMenu, _model.IconSize.ToString());
            FenceMenu.PlacementTarget = fe;
            FenceMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            FenceMenu.IsOpen = true;
        }

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

                // A portal mirrors the user's real folder — renaming only changes the
                // display label; never move/rename the underlying folder.
                if (_model.IsPortal)
                {
                    _model.Name = newName;
                    TitleText.Text = "🗁 " + _model.Name;
                    FenceRenamed?.Invoke(this, EventArgs.Empty);
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
                Changed?.Invoke(this, EventArgs.Empty);  // persist transparency
            }
        }

        private void TitleBar_ContextMenuOpening(object sender, ContextMenuEventArgs e)
        {
            // Tick the active sort / icon-size options.
            CheckByTag(SortMenu, _model.Sort.ToString());
            CheckByTag(IconSizeMenu, _model.IconSize.ToString());
        }

        private static void CheckByTag(MenuItem? parent, string activeTag)
        {
            if (parent is null) return;
            foreach (var obj in parent.Items)
                if (obj is MenuItem mi)
                    mi.IsChecked = string.Equals(Convert.ToString(mi.Tag), activeTag, StringComparison.Ordinal);
        }

        private void DeleteFence_Click(object sender, RoutedEventArgs e)
        {
            // MainWindow owns the single confirm + folder handling (portal-aware).
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
