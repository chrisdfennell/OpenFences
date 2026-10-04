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
        // Portals watch their mirrored folder. Real-icon fences own desktop icons by
        // name and have no backing folder, so the watcher is null for them.
        private FileSystemWatcher? _watcher;

        // Portals: the folder currently shown. Starts at the portal's folder and changes as the
        // user opens subfolders (not persisted; a portal always reopens at its own folder).
        private string _currentFolder = "";

        // A "real" fence == every non-portal fence. It renders the desktop items it owns
        // (_model.ItemPaths) as tiles, like a portal scoped to specific desktop items.
        private bool IsRealIcon => !_model.IsPortal;


        private const double TitleBarHeight = 34;

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


        /// <summary>App-wide options (snapping, hover roll-up…), wired by MainWindow.</summary>
        public static AppOptions? Options;

        /// <summary>Every open fence window, so fences can snap to and search across each other.</summary>
        public static readonly List<FenceWindow> AllFences = new();

        public FenceWindow(FenceModel model)
        {
            InitializeComponent();
            _model = model;
            AllFences.Add(this);
            Closed += (_, __) => AllFences.Remove(this);

            TitleText.Text = model.IsPortal ? "🗁 " + model.Name : model.Name;
            Left = model.Left; Top = model.Top;
            Width = model.Width; Height = model.Height;
            FitToCurrentScreens();

            ApplyBackground();
            ApplyLock();

            // Both portal and real fences render their items as tiles in the scrollable
            // content area, and both accept drops.
            AllowDrop = true;
            DragEnter += FenceWindow_DragEnter;
            DragOver += FenceWindow_DragOver;
            MouseEnter += FenceWindow_MouseEnter;
            IsVisibleChanged += (_, __) => UpdateBackgroundPlayback();
            MouseLeave += FenceWindow_MouseLeave;
            Drop += FenceWindow_Drop;
            Items.ItemsSource = ItemsSource;

            if (_model.IsPortal)
            {
                // ----- Portal: windowed view of a real folder -----
                NavigatePortal(_model.FolderPath);
            }
            else
            {
                // ----- Real fence: renders the desktop items it owns (by path) as tiles -----
                ReloadRealItems();
            }

            if (_model.Collapsed) SetCollapsed(true, animate: false);

            // Keep fences out of the Alt-Tab switcher (must run once the HWND exists), and
            // watch the move/resize loop for snapping and per-monitor-layout memory.
            SourceInitialized += (_, __) =>
            {
                var hwnd = new WindowInteropHelper(this).Handle;
                DesktopHelper.HideFromAltTab(hwnd);
                HwndSource.FromHwnd(hwnd)?.AddHook(WndProc);
                ApplyGlass();
            };

            Loaded += (_, __) => EnsureBottomZOrder();
            // Clicking a fence: back below app windows, but in front of the other fences.
            Activated += (_, __) => { EnsureBottomZOrder(); BringToFrontOfFences(); };

            LocationChanged += SaveGeometry;
            SizeChanged += (_, __) => { SaveGeometry(null, null); if (_model.Glass) UpdateGlassRegion(); };

            // Stop watching the backing folder once this fence is gone.
            Closed += (_, __) =>
            {
                _hoverTimer?.Stop();
                _rollTimer?.Stop();
                ReleaseMedia();
                try { _watcher?.Dispose(); } catch { /* ignore */ }
            };
        }

        // ---------- UI/Background ----------

        private static readonly MediaColor DefaultBody = MediaColor.FromRgb(0x20, 0x20, 0x20);  // #202020
        private static readonly MediaColor DefaultTitle = MediaColor.FromRgb(0x2B, 0x2B, 0x2B); // #2B2B2B

        private void ApplyBackground()
        {
            // An accent tints the dark base (strongly on the title bar, lightly on the body), so
            // light text stays readable whatever color is picked.
            MediaColor body = DefaultBody, title = DefaultTitle;
            if (TryParseColor(_model.AccentColor, out var accent))
            {
                body = Mix(accent, DefaultBody, 0.16);
                title = Mix(accent, DefaultTitle, 0.55);
            }

            double opacity = Math.Clamp(_model.BackgroundOpacity, 0.0, 1.0);
            var media = MediaBrush();
            if (media != null)
            {
                // Picture/video background: it fades with the fence's transparency, and a
                // darkening layer in the fence's body color keeps the labels readable.
                media.Opacity = opacity;
                MediaLayer.Background = media;
                // In Fit mode the picture doesn't cover the whole fence; the bands around it show
                // the fence's normal color. Fill/Stretch cover everything, so nothing goes under.
                RootBorder.Background = _model.BackgroundFit == FenceBackgroundFit.Fit
                    ? new SolidColorBrush(MediaColor.FromArgb((byte)Math.Round(255 * opacity), body.R, body.G, body.B))
                    : System.Windows.Media.Brushes.Transparent;
                byte dim = (byte)Math.Round(255 * Math.Clamp(_model.BackgroundDim, 0.0, 1.0) * opacity);
                TintLayer.Background = new SolidColorBrush(MediaColor.FromArgb(dim, body.R, body.G, body.B));
            }
            else
            {
                byte a = (byte)Math.Round(255 * opacity);
                RootBorder.Background = new SolidColorBrush(MediaColor.FromArgb(a, body.R, body.G, body.B));
                MediaLayer.Background = System.Windows.Media.Brushes.Transparent;
                TintLayer.Background = System.Windows.Media.Brushes.Transparent;
            }
            TitleBar.Background = new SolidColorBrush(title);
            TitleText.FontSize = _model.TitleFontSize > 0 ? _model.TitleFontSize : 12;
        }

        // ---------- Background picture / video ----------
        private static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };
        private System.Windows.Media.Brush? _mediaBrush;
        private string? _mediaBrushKey;   // path + fit the brush was built for
        private MediaPlayer? _bgPlayer;

        private static bool IsVideo(string path) =>
            Array.IndexOf(VideoExtensions, Path.GetExtension(path).ToLowerInvariant()) >= 0;

        /// <summary>The brush for the fence's background picture/video, or null when it has
        /// none (or the file is gone). Built once per path/fit and reused.</summary>
        private System.Windows.Media.Brush? MediaBrush()
        {
            var path = _model.BackgroundMedia;
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                ReleaseMedia();
                return null;
            }

            var key = path + "|" + _model.BackgroundFit;
            if (_mediaBrush != null && _mediaBrushKey == key) return _mediaBrush;
            ReleaseMedia();

            var stretch = _model.BackgroundFit switch
            {
                FenceBackgroundFit.Fit => Stretch.Uniform,
                FenceBackgroundFit.Stretch => Stretch.Fill,
                _ => Stretch.UniformToFill
            };

            try
            {
                if (IsVideo(path))
                {
                    // MediaPlayer + VideoDrawing plays without being in the visual tree; looping,
                    // muted unless sound was turned on for this fence.
                    var player = new MediaPlayer { IsMuted = _model.BackgroundMuted, Volume = 0.5 };
                    var drawing = new VideoDrawing { Player = player, Rect = new Rect(0, 0, 16, 9) };
                    player.MediaOpened += (_, __) =>
                    {
                        if (player.NaturalVideoWidth > 0 && player.NaturalVideoHeight > 0)
                            drawing.Rect = new Rect(0, 0, player.NaturalVideoWidth, player.NaturalVideoHeight);
                    };
                    player.MediaEnded += (_, __) => { player.Position = TimeSpan.Zero; player.Play(); };
                    player.MediaFailed += (_, e) =>
                        (System.Windows.Application.Current as App)?.SafeLog("Background video", e.ErrorException);
                    player.Open(new Uri(path));
                    _bgPlayer = player;
                    _mediaBrush = new DrawingBrush(drawing) { Stretch = stretch };
                    UpdateBackgroundPlayback();
                }
                else
                {
                    // Decode at a sensible size: big photos would otherwise sit in memory at full size.
                    var bmp = new System.Windows.Media.Imaging.BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(path);
                    bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                    bmp.DecodePixelWidth = (int)Math.Min(2560, 1600 * OpenFences.Services.ScreenLayout.Scale);
                    bmp.EndInit();
                    bmp.Freeze();
                    _mediaBrush = new ImageBrush(bmp) { Stretch = stretch };
                }
                _mediaBrushKey = key;
                return _mediaBrush;
            }
            catch (Exception ex)
            {
                (System.Windows.Application.Current as App)?.SafeLog("Fence background", ex);
                ReleaseMedia();
                return null;
            }
        }

        private void ReleaseMedia()
        {
            try { _bgPlayer?.Close(); } catch { /* ignore */ }
            _bgPlayer = null;
            _mediaBrush = null;
            _mediaBrushKey = null;
        }

        /// <summary>Videos only play while the fence's body can be seen: not while the fence is
        /// hidden or rolled up (unless it's peeking open).</summary>
        private void UpdateBackgroundPlayback()
        {
            if (_bgPlayer == null) return;
            bool showing = IsVisible && (!_model.Collapsed || _tempExpanded);
            if (showing) _bgPlayer.Play();
            else _bgPlayer.Pause();
        }

        private void ChooseBackground_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a background picture or video",
                Filter = "Pictures and videos|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff;*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm" +
                         "|Pictures|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.tif;*.tiff" +
                         "|Videos|*.mp4;*.m4v;*.mov;*.wmv;*.avi;*.mkv;*.webm"
            };
            if (dlg.ShowDialog(this) != true) return;

            _model.BackgroundMedia = dlg.FileName;
            ApplyBackground();
            if (_mediaBrush == null)
            {
                _model.BackgroundMedia = null;
                MessageBox.Show("That file couldn't be used as a background.", "OpenFences",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                ApplyBackground();
                return;
            }
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // Per-fence: turn a background video's sound on or off.
        private void MuteBackground_Click(object sender, RoutedEventArgs e)
        {
            _model.BackgroundMuted = !_model.BackgroundMuted;
            if (_bgPlayer != null) _bgPlayer.IsMuted = _model.BackgroundMuted;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void RemoveBackground_Click(object sender, RoutedEventArgs e)
        {
            _model.BackgroundMedia = null;
            ApplyBackground();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void BackgroundFit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && Enum.TryParse<FenceBackgroundFit>(Convert.ToString(mi.Tag), out var fit))
            {
                _model.BackgroundFit = fit;
                ApplyBackground();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void BackgroundDim_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi &&
                double.TryParse(Convert.ToString(mi.Tag), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var dim))
            {
                _model.BackgroundDim = dim;
                ApplyBackground();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private static MediaColor Mix(MediaColor c, MediaColor baseColor, double amount) => MediaColor.FromRgb(
            (byte)Math.Round(baseColor.R + (c.R - baseColor.R) * amount),
            (byte)Math.Round(baseColor.G + (c.G - baseColor.G) * amount),
            (byte)Math.Round(baseColor.B + (c.B - baseColor.B) * amount));

        private static bool TryParseColor(string? hex, out MediaColor color)
        {
            color = default;
            if (string.IsNullOrWhiteSpace(hex)) return false;
            try { color = (MediaColor)System.Windows.Media.ColorConverter.ConvertFromString(hex); return true; }
            catch { return false; }
        }

        // ---------- Real fence: owned desktop items (by path) ----------

        public string FenceName => _model.Name;

        /// <summary>The launchable paths of the desktop items this real fence owns.</summary>
        public IReadOnlyList<string> OwnedItemPaths => _model.ItemPaths;

        /// <summary>Wired by MainWindow so a drop can transfer items away from other fences and
        /// persist. Given (targetFence, paths), it assigns those paths to the target.</summary>
        public static Action<FenceWindow, IReadOnlyList<string>>? RequestAssignItems;

        public bool OwnsPath(string path) =>
            _model.ItemPaths.Contains(path, StringComparer.OrdinalIgnoreCase);

        /// <summary>Add desktop items (by path) to this fence and re-render. Does not remove them
        /// from other fences — go through MainWindow (RequestAssignItems) for that.</summary>
        public void AddItems(IEnumerable<string> paths)
        {
            bool any = false;
            foreach (var p in paths)
            {
                if (string.IsNullOrWhiteSpace(p)) continue;
                if (!_model.ItemPaths.Contains(p, StringComparer.OrdinalIgnoreCase))
                {
                    _model.ItemPaths.Add(p);
                    any = true;
                }
            }
            if (any) { Changed?.Invoke(this, EventArgs.Empty); ReloadRealItems(); }
        }

        /// <summary>The item was renamed on disk: keep it in this fence (same position) under its
        /// new path. Returns false if this fence doesn't own <paramref name="oldPath"/>.</summary>
        public bool ReplaceItemPath(string oldPath, string newPath)
        {
            int i = _model.ItemPaths.FindIndex(p => string.Equals(p, oldPath, StringComparison.OrdinalIgnoreCase));
            if (i < 0) return false;
            _model.ItemPaths[i] = newPath;
            Changed?.Invoke(this, EventArgs.Empty);
            ReloadRealItems();
            return true;
        }

        /// <summary>Remove a desktop item (by path) from this fence and re-render.</summary>
        public bool RemoveItemPath(string path)
        {
            int removed = _model.ItemPaths.RemoveAll(
                p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            if (removed > 0) { Changed?.Invoke(this, EventArgs.Empty); ReloadRealItems(); return true; }
            return false;
        }

        /// <summary>Rebuild this real fence's tiles from the desktop items it owns.</summary>
        public void ReloadRealItems()
        {
            if (IsPortal) return;

            var paths = SortEntries(_model.ItemPaths
                .Where(p => !string.IsNullOrWhiteSpace(p)).ToList());

            ItemsSource.Clear();
            foreach (var path in paths)
            {
                ItemsSource.Add(new FenceItem
                {
                    Path = path,
                    DisplayName = LabelFor(path)
                });
            }

            LoadIconsAsync();
        }

        /// <summary>Tile label: desktop-style name (shortcut extensions hidden), and without any
        /// file extension when Settings → Hide file extensions is on.</summary>
        private static string LabelFor(string path)
        {
            var label = OpenFences.Services.DesktopItems.LabelFor(path);
            if (Options?.HideFileExtensions == true && !path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) &&
                !Directory.Exists(path))
                label = Path.GetFileNameWithoutExtension(label);
            return label;
        }

        /// <summary>Re-read labels and icons (after a display setting changes).</summary>
        public void RefreshItems()
        {
            if (IsPortal) ReloadItems();
            else ReloadRealItems();
        }

        public void EnsureBottomZOrder()
        {
            DesktopHelper.SendToDesktopLayer(new WindowInteropHelper(this).Handle);
        }

        /// <summary>Stack this fence above every other fence (still below normal app windows),
        /// so an opened roll-up or a clicked fence isn't hidden behind its neighbours.</summary>
        public void BringToFrontOfFences()
        {
            var others = AllFences
                .Where(f => f != this && f.IsVisible)
                .Select(f => new WindowInteropHelper(f).Handle)
                .Where(h => h != IntPtr.Zero)
                .ToHashSet();
            DesktopHelper.RaiseAbove(new WindowInteropHelper(this).Handle, others);
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

            ScheduleSave();
        }

        // ---------- Monitor arrangements ----------
        private const int WM_EXITSIZEMOVE = 0x0232;

        private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            // While Windows' item menu is open, let it fill submenus (Send to, Open with…).
            if (OpenFences.Services.ShellContextMenu.HandleMenuMessage(msg, wParam, lParam, out var menuResult))
            {
                handled = true;
                return menuResult;
            }

            switch (msg)
            {
                case OpenFences.Services.FenceSnapper.WM_MOVING:
                case OpenFences.Services.FenceSnapper.WM_SIZING:
                    SnapProposedRect(hwnd, msg == OpenFences.Services.FenceSnapper.WM_SIZING ? wParam.ToInt32() : 0, lParam);
                    break;

                // Only a user move/resize is remembered for this monitor arrangement. Windows
                // also moves fences itself when a monitor disappears; that must not overwrite
                // the spot the user chose for the arrangement it came from.
                case WM_EXITSIZEMOVE:
                    RememberPlacementForScreens();
                    break;
            }
            return IntPtr.Zero;
        }

        private void SnapProposedRect(IntPtr self, int sizingEdge, IntPtr lParam)
        {
            var opts = Options;
            if (opts == null || lParam == IntPtr.Zero) return;

            var r = System.Runtime.InteropServices.Marshal.PtrToStructure<OpenFences.Services.FenceSnapper.RECT>(lParam);
            var others = AllFences
                .Where(f => f != this && f.IsVisible)
                .Select(f => new WindowInteropHelper(f).Handle)
                .Where(h => h != IntPtr.Zero && h != self)
                .ToList();

            if (OpenFences.Services.FenceSnapper.Snap(ref r, sizingEdge, others, opts.SnapToEdges, opts.SnapToGrid))
                System.Runtime.InteropServices.Marshal.StructureToPtr(r, lParam, false);
        }

        private void RememberPlacementForScreens()
        {
            _model.Layouts[OpenFences.Services.ScreenLayout.CurrentKey()] = new FenceRect
            {
                Left = _model.Left,
                Top = _model.Top,
                Width = _model.Width,
                Height = _model.Height
            };
            ScheduleSave();
        }

        /// <summary>Monitors changed (or startup): go back to where the user put this fence for
        /// this arrangement, and make sure it's on a visible screen either way.
        /// Returns true if the fence moved.</summary>
        public bool FitToCurrentScreens()
        {
            var r = _model.Layouts.TryGetValue(OpenFences.Services.ScreenLayout.CurrentKey(), out var saved)
                ? new Rect(saved.Left, saved.Top, saved.Width, saved.Height)
                : new Rect(_model.Left, _model.Top, _model.Width, _model.Height);
            r = OpenFences.Services.ScreenLayout.FitOnScreen(r);

            if (r.Left == _model.Left && r.Top == _model.Top &&
                r.Width == _model.Width && r.Height == _model.Height) return false;

            _model.Left = r.Left; _model.Top = r.Top;
            _model.Width = r.Width; _model.Height = r.Height;

            _suppressGeometrySave = true;
            Left = r.Left; Top = r.Top; Width = r.Width;
            if (!_model.Collapsed) Height = r.Height;
            _suppressGeometrySave = false;
            return true;
        }

        // Moving/resizing fires many events; persist once the fence has settled, so the layout
        // survives a crash or an upgrade instead of only being saved on a clean exit.
        private System.Windows.Threading.DispatcherTimer? _saveTimer;

        private void ScheduleSave()
        {
            if (!IsLoaded) return; // initial placement in the constructor isn't a user change
            if (_saveTimer == null)
            {
                _saveTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(600) };
                _saveTimer.Tick += (_, __) => { _saveTimer.Stop(); Changed?.Invoke(this, EventArgs.Empty); };
            }
            _saveTimer.Stop();
            _saveTimer.Start();
        }

        // ---------- Portal navigation ----------

        /// <summary>Show <paramref name="folder"/> (the portal's folder or one inside it) and
        /// watch it for changes. The folder may be gone or on a drive that isn't connected: never
        /// recreate it (that would leave an empty stand-in), just show the portal as unavailable.</summary>
        private void NavigatePortal(string folder)
        {
            try { _watcher?.Dispose(); } catch { /* ignore */ }
            _watcher = null;
            _currentFolder = folder;
            TitleText.Text = "🗁 " + _model.Name;
            TitleText.ToolTip = _model.FolderPath;

            try
            {
                if (!Directory.Exists(folder))
                    throw new DirectoryNotFoundException(folder);

                ReloadItems();

                _watcher = new FileSystemWatcher(folder)
                {
                    IncludeSubdirectories = false,
                    EnableRaisingEvents = true
                };
                _watcher.Created += (_, __) => Dispatcher.Invoke(ReloadItems);
                _watcher.Deleted += (_, __) => Dispatcher.Invoke(ReloadItems);
                _watcher.Renamed += (_, __) => Dispatcher.Invoke(ReloadItems);
            }
            catch
            {
                _watcher?.Dispose();
                _watcher = null;
                ItemsSource.Clear();
                TitleText.Text = "🗁 " + _model.Name + " (unavailable)";
                TitleText.ToolTip = $"This folder can't be reached:\n{folder}\n\n" +
                                    "Reconnect the drive (or restore the folder) and restart OpenFences.";
            }

            UpdateBreadcrumbs();
        }

        private bool IsInSubfolder =>
            _model.IsPortal && !string.Equals(Path.GetFullPath(_currentFolder).TrimEnd('\\'),
                                              Path.GetFullPath(_model.FolderPath).TrimEnd('\\'),
                                              StringComparison.OrdinalIgnoreCase);

        /// <summary>Back/breadcrumb bar under the title, shown only below the portal's own folder.</summary>
        private void UpdateBreadcrumbs()
        {
            Crumbs.Children.Clear();
            if (!IsInSubfolder)
            {
                BreadcrumbBar.Visibility = Visibility.Collapsed;
                return;
            }
            BreadcrumbBar.Visibility = Visibility.Visible;

            var root = Path.GetFullPath(_model.FolderPath).TrimEnd('\\');
            var rel = Path.GetRelativePath(root, _currentFolder);
            var parts = rel.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries);

            AddCrumb(_model.Name, root, isLast: false);
            string path = root;
            for (int i = 0; i < parts.Length; i++)
            {
                path = Path.Combine(path, parts[i]);
                Crumbs.Children.Add(new TextBlock { Text = "›", Foreground = System.Windows.Media.Brushes.Gray, Margin = new Thickness(2, 0, 2, 0), VerticalAlignment = VerticalAlignment.Center });
                AddCrumb(parts[i], path, isLast: i == parts.Length - 1);
            }
        }

        private void AddCrumb(string label, string folder, bool isLast)
        {
            // The title-button template ignores Padding, so the label carries its own margin.
            var b = new System.Windows.Controls.Button
            {
                Content = new TextBlock { Text = label, Margin = new Thickness(6, 0, 6, 0) },
                Style = (Style)FindResource("FenceTitleButton"),
                FontSize = 12,
                Height = 22,
                FontWeight = isLast ? FontWeights.SemiBold : FontWeights.Normal,
                ToolTip = folder
            };
            if (!isLast) b.Click += (_, __) => NavigatePortal(folder);
            Crumbs.Children.Add(b);
        }

        private void PortalUp()
        {
            if (!IsInSubfolder) return;
            var parent = Path.GetDirectoryName(_currentFolder);
            if (!string.IsNullOrEmpty(parent)) NavigatePortal(parent);
        }

        private void Back_Click(object sender, RoutedEventArgs e) => PortalUp();

        /// <summary>Point this portal at a different folder (menu or folder drop).</summary>
        private void RetargetPortal(string folder)
        {
            if (!_model.IsPortal || !Directory.Exists(folder)) return;

            // A portal still named after its old folder follows the new one.
            var oldName = new DirectoryInfo(_model.FolderPath).Name;
            if (string.Equals(_model.Name, oldName, StringComparison.OrdinalIgnoreCase))
                _model.Name = new DirectoryInfo(folder).Name;

            _model.FolderPath = folder;
            NavigatePortal(folder);
            FenceRenamed?.Invoke(this, EventArgs.Empty); // persists
        }

        private void ChangePortalFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new Microsoft.Win32.OpenFolderDialog
            {
                Title = "Choose the folder this portal shows",
                InitialDirectory = Directory.Exists(_model.FolderPath) ? _model.FolderPath : ""
            };
            if (dlg.ShowDialog() == true) RetargetPortal(dlg.FolderName);
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
                    entries.AddRange(Directory.EnumerateDirectories(_currentFolder));

                entries.AddRange(Directory.EnumerateFiles(_currentFolder)
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
                    DisplayName = LabelFor(path)
                });
            }

            LoadIconsAsync();
        }

        /// <summary>Resolve tile icons off the UI thread so big fences don't freeze. This MUST run
        /// on an STA thread: IconHelper uses apartment-threaded shell COM (IShellLink), which is
        /// unreliable on MTA thread-pool threads and silently drops icons. IconHelper freezes the
        /// ImageSources, so they're safe to hand back to the UI thread.</summary>
        private void LoadIconsAsync()
        {
            var snapshot = ItemsSource.ToList();
            bool thumbnails = Options?.ShowThumbnails ?? true;
            int thumbPx = (int)Math.Round(IconPx * OpenFences.Services.ScreenLayout.Scale * 2); // room for crisp scaling
            var loader = new System.Threading.Thread(() =>
            {
                foreach (var item in snapshot)
                {
                    var icon = (thumbnails && OpenFences.Services.ShellThumbnail.HasPreview(item.Path)
                                   ? OpenFences.Services.ShellThumbnail.Get(item.Path, thumbPx)
                                   : null)
                               ?? OpenFences.Services.IconHelper.GetImageSourceForPath(item.Path);
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
                if (IsRealIcon) ReloadRealItems();
                else ReloadItems();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void IconSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi && Enum.TryParse<FenceIconSize>(Convert.ToString(mi.Tag), out var size))
            {
                _model.IconSize = size;
                RaiseLayoutMetricsChanged();   // tiles rebind their width/height from IconPx
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        // ---------- Search ----------
        /// <summary>Fade out items that don't match <paramref name="query"/> (null clears), and
        /// hold a rolled-up fence open while it has matches. Returns the number of matches.</summary>
        public int ApplySearch(string? query)
        {
            int matches = 0;
            foreach (var item in ItemsSource)
            {
                bool match = string.IsNullOrEmpty(query) ||
                             item.DisplayName.Contains(query, StringComparison.OrdinalIgnoreCase);
                item.IsDimmed = !string.IsNullOrEmpty(query) && !match;
                if (match && !string.IsNullOrEmpty(query)) matches++;
            }
            HoldOpen(matches > 0);
            return matches;
        }

        /// <summary>While searching, fences come up above app windows so matches are visible.</summary>
        public void SetRaised(bool raised)
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            DesktopHelper.SetTopmost(hwnd, raised);
            if (!raised) EnsureBottomZOrder();
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
            if (MenuSenderToItem(sender) is FenceItem item) LaunchPath(item.Path);
        }

        private void Item_OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            if (MenuSenderToItem(sender) is not FenceItem item) return;
            if (item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return; // no containing folder
            try
            {
                var dir = Path.GetDirectoryName(item.Path);
                if (!string.IsNullOrEmpty(dir))
                    Process.Start(new ProcessStartInfo { FileName = dir, UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        // Launch a desktop item: a real file/folder via ShellExecute, or a special item via its
        // shell:::{CLSID} moniker (opened through Explorer).
        internal static void LaunchPath(string path)
        {
            try
            {
                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
                    Process.Start(new ProcessStartInfo("explorer.exe", path) { UseShellExecute = true });
                else
                    Process.Start(new ProcessStartInfo(path) { UseShellExecute = true });
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
            try { if (_watcher != null) _watcher.EnableRaisingEvents = enabled; } catch { /* ignore */ }
        }

        // ---------- Title bar ----------

        private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
                ToggleCollapsed();
            else if (e.LeftButton == MouseButtonState.Pressed && !_model.Locked)
                DragMove();
        }

        // ---------- Frosted glass ----------
        private void Glass_Click(object sender, RoutedEventArgs e)
        {
            _model.Glass = !_model.Glass;
            // Glass needs a see-through body to show; nudge a nearly opaque fence down.
            if (_model.Glass && _model.BackgroundOpacity > 0.7) _model.BackgroundOpacity = 0.6;
            ApplyBackground();
            ApplyGlass();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void ApplyGlass()
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;
            OpenFences.Services.WindowBlur.Set(hwnd, _model.Glass);
            if (_model.Glass) UpdateGlassRegion();
        }

        // The blur covers the whole window rectangle; clip it to the card's rounded corners.
        private void UpdateGlassRegion()
        {
            double s = OpenFences.Services.ScreenLayout.Scale;
            OpenFences.Services.WindowBlur.SetRoundedRegion(new WindowInteropHelper(this).Handle,
                (int)Math.Round(ActualWidth * s), (int)Math.Round(ActualHeight * s), (int)Math.Round(10 * s));
        }

        // ---------- Lock ----------
        private void Lock_Click(object sender, RoutedEventArgs e)
        {
            _model.Locked = !_model.Locked;
            ApplyLock();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // Locked: no dragging (title bar) and no resize grips.
        private void ApplyLock()
        {
            // Work on a copy: the chrome from XAML may be frozen.
            if (System.Windows.Shell.WindowChrome.GetWindowChrome(this) is { } chrome)
            {
                var updated = (System.Windows.Shell.WindowChrome)chrome.Clone();
                updated.ResizeBorderThickness = _model.Locked ? new Thickness(0) : new Thickness(6);
                System.Windows.Shell.WindowChrome.SetWindowChrome(this, updated);
            }
            LockGlyph.Visibility = _model.Locked ? Visibility.Visible : Visibility.Collapsed;
        }

        private void SetCollapsed(bool collapsed, bool animate)
        {
            _model.Collapsed = collapsed;
            UpdateBackgroundPlayback();

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

        // ---------- Roll-up: collapsed fences open temporarily on hover / drag-over ----------
        // While "temp-expanded" the model stays Collapsed, so nothing is persisted; a poll timer
        // closes it again once the cursor has left (IsMouseOver is unreliable during drag/drop).
        private bool _tempExpanded;
        private bool _holdOpen;
        private System.Windows.Threading.DispatcherTimer? _hoverTimer;
        private System.Windows.Threading.DispatcherTimer? _rollTimer;

        private void FenceWindow_MouseEnter(object sender, MouseEventArgs e)
        {
            // Pointing at a rolled-up fence brings it (title bar included) in front of others.
            if (_model.Collapsed) BringToFrontOfFences();

            if (!_model.Collapsed || _tempExpanded || Options?.ExpandCollapsedOnHover != true) return;
            _hoverTimer ??= NewTimer(350, () =>
            {
                _hoverTimer!.Stop();
                if (CursorInside()) TempExpand(true);
            });
            _hoverTimer.Start();
        }

        private void FenceWindow_MouseLeave(object sender, MouseEventArgs e) => _hoverTimer?.Stop();

        /// <summary>Keep a collapsed fence open (e.g. while search shows its matches).
        /// Releasing lets it roll back up once the cursor isn't over it.</summary>
        public void HoldOpen(bool hold)
        {
            _holdOpen = hold;
            if (hold) TempExpand(true);
        }

        private void TempExpand(bool open)
        {
            if (open)
            {
                if (!_model.Collapsed || _tempExpanded) return;
                _tempExpanded = true;
                UpdateBackgroundPlayback();
                BringToFrontOfFences(); // in front of neighbouring fences
                Scroller.Visibility = Visibility.Visible;
                AnimateHeight(Math.Max(_model.Height, MinExpandedHeight), onCompleted: null);

                _rollTimer ??= NewTimer(400, () =>
                {
                    if (_holdOpen || CursorInside() || _marqueeActive || AnyMenuOpen()) return;
                    TempExpand(false);
                });
                _rollTimer.Start();
            }
            else
            {
                if (!_tempExpanded) return;
                _tempExpanded = false;
                UpdateBackgroundPlayback();
                _rollTimer?.Stop();
                if (_model.Collapsed)
                    AnimateHeight(CollapsedHeight, onCompleted: () => Scroller.Visibility = Visibility.Collapsed);
            }
        }

        private System.Windows.Threading.DispatcherTimer NewTimer(int ms, Action tick)
        {
            var t = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(ms) };
            t.Tick += (_, __) => tick();
            return t;
        }

        private bool CursorInside()
        {
            var p = System.Windows.Forms.Cursor.Position;
            double s = OpenFences.Services.ScreenLayout.Scale;
            return p.X >= Left * s && p.X < (Left + ActualWidth) * s &&
                   p.Y >= Top * s && p.Y < (Top + ActualHeight) * s;
        }

        // Item right-click menus are ContextMenus too, but they close when the cursor leaves.
        private bool AnyMenuOpen() => FenceMenu?.IsOpen == true || _dropMenu?.IsOpen == true;

        private void Collapse_Click(object sender, RoutedEventArgs e) => ToggleCollapsed();

        // User-driven collapse/expand. Bring this fence to the front of the other fences (still
        // in the desktop layer) first, so expanding never leaves its body hidden behind a
        // neighbouring fence. Activated only re-stacks when the fence wasn't already active,
        // so re-stack explicitly too.
        private void ToggleCollapsed()
        {
            Activate();
            Focus();
            EnsureBottomZOrder();
            BringToFrontOfFences();

            // Opened by hover: collapsing again would feel like nothing happened, so keep it open.
            if (_tempExpanded)
            {
                _tempExpanded = false;
                _rollTimer?.Stop();
                _model.Collapsed = false;
                ScheduleSave();
                return;
            }

            SetCollapsed(!_model.Collapsed, animate: true);
            ScheduleSave();
        }


        // ---------- Items ----------

        private void Item_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not FenceItem item) return;
            Scroller.Focus();

            _cursorIndex = ItemsSource.IndexOf(item);
            if (e.ClickCount == 2) { OpenItem(item); return; }

            // Remember where a possible drag starts.
            _dragItem = item;
            _dragStart = e.GetPosition(this);
            _deferredSelectOnly = null;

            // Ctrl+click toggles; plain click selects just this one. Clicking an item that's
            // already part of a multi-selection keeps the selection until mouse-up, so the whole
            // selection can be dragged.
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
                item.IsSelected = !item.IsSelected;
            else if (item.IsSelected && SelectedCount > 1)
                _deferredSelectOnly = item;
            else
                SelectOnly(item);
        }

        private void Item_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (_deferredSelectOnly != null) SelectOnly(_deferredSelectOnly); // a click, not a drag
            _deferredSelectOnly = null;
            _dragItem = null;
        }

        // ---------- Dragging tiles ----------
        // Tiles drag out as real files (Explorer, mail, apps) and as OpenFences items, so another
        // fence takes them over and the same fence reorders them.
        private const string ItemPathsFormat = "OpenFences.ItemPaths";
        private static FenceWindow? _dragSource;
        private FenceItem? _dragItem;
        private FenceItem? _deferredSelectOnly;
        private Point _dragStart;

        private void Item_MouseMove(object sender, MouseEventArgs e)
        {
            if (_dragItem == null || e.LeftButton != MouseButtonState.Pressed || sender is not DependencyObject source) return;

            var pos = e.GetPosition(this);
            if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
                Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

            _deferredSelectOnly = null;
            if (!_dragItem.IsSelected) SelectOnly(_dragItem);
            var paths = ItemsSource.Where(i => i.IsSelected).Select(i => i.Path).ToArray();
            _dragItem = null;
            if (paths.Length == 0) return;

            var data = new System.Windows.DataObject();
            data.SetData(ItemPathsFormat, paths);
            // Special items (This PC, …) aren't files; only real paths go to other apps.
            var files = paths.Where(p => !p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)).ToArray();
            if (files.Length > 0)
                data.SetData(System.Windows.DataFormats.FileDrop, files);

            _dragSource = this;
            try
            {
                System.Windows.DragDrop.DoDragDrop(source, data,
                    System.Windows.DragDropEffects.Move | System.Windows.DragDropEffects.Copy | System.Windows.DragDropEffects.Link);
            }
            catch (Exception ex)
            {
                (System.Windows.Application.Current as App)?.SafeLog("Drag tiles", ex);
            }
            finally { _dragSource = null; }
        }

        /// <summary>Move the dragged paths to just before <paramref name="target"/> (or to the end)
        /// and switch the fence to Manual sort so the order sticks.</summary>
        private void ReorderItems(IReadOnlyCollection<string> moving, FenceItem? target)
        {
            if (!IsRealIcon) return;

            // Start from what's on screen, so switching from another sort keeps the visible order.
            var order = ItemsSource.Select(i => i.Path).ToList();
            foreach (var p in _model.ItemPaths)
                if (!order.Contains(p, StringComparer.OrdinalIgnoreCase)) order.Add(p);

            var set = new HashSet<string>(moving, StringComparer.OrdinalIgnoreCase);
            if (target != null && set.Contains(target.Path)) return; // dropped onto itself

            var moved = order.Where(set.Contains).ToList();
            order.RemoveAll(set.Contains);
            int at = target == null ? order.Count
                                    : Math.Max(0, order.FindIndex(p => string.Equals(p, target.Path, StringComparison.OrdinalIgnoreCase)));
            order.InsertRange(at, moved);

            _model.ItemPaths = order;
            _model.Sort = FenceSort.Manual;
            ReloadRealItems();
            foreach (var i in ItemsSource) i.IsSelected = set.Contains(i.Path);
            Changed?.Invoke(this, EventArgs.Empty);
        }

        // Right-clicking an unselected item selects just it (so the menu acts on it).
        private void Item_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is FrameworkElement fe && fe.DataContext is FenceItem item && !item.IsSelected)
                SelectOnly(item);
        }

        // Right-click shows Windows' own menu for the selection (Open with, Send to, Properties…)
        // with "Remove from fence" on top; special items like This PC use our own menu.
        private void Item_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is not FrameworkElement fe || fe.DataContext is not FenceItem item) return;
            e.Handled = true;

            var paths = ItemsSource.Where(i => i.IsSelected).Select(i => i.Path).ToList();
            if (paths.Count == 0) paths.Add(item.Path);

            bool special = paths.Any(p => p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase));
            if (!special)
            {
                var extras = new List<OpenFences.Services.ShellContextMenu.Extra>();
                if (CanRemoveSelected)
                    extras.Add(new("Remove from fence", () => RequestRemoveSelected?.Invoke()));

                var hwnd = new WindowInteropHelper(this).Handle;
                if (OpenFences.Services.ShellContextMenu.Show(hwnd, paths, extras, () => RenameItem(item)))
                    return;
            }

            if (fe.ContextMenu is ContextMenu menu)
            {
                menu.PlacementTarget = fe;
                menu.DataContext = item;
                menu.IsOpen = true;
            }
        }

        // ---------- Rename items ----------
        /// <summary>Rename the real file/folder behind a tile. Shortcut extensions (.lnk/.url)
        /// stay hidden, like on the desktop. A renamed item stays in this fence.</summary>
        private void RenameItem(FenceItem item)
        {
            var path = item.Path;
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return;

            bool isDir = Directory.Exists(path);
            string ext = Path.GetExtension(path);
            bool hideExt = !isDir && (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) ||
                                      ext.Equals(".url", StringComparison.OrdinalIgnoreCase));
            string current = hideExt ? Path.GetFileNameWithoutExtension(path) : Path.GetFileName(path);

            var prompt = new InputDialog("Rename", "New name:", current) { Owner = this };
            if (prompt.ShowDialog() != true) return;

            var name = prompt.Value.Trim();
            if (name.Length == 0 || name == current) return;
            if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                MessageBox.Show("A name can't contain any of these characters:\n\\ / : * ? \" < > |",
                                "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var dir = Path.GetDirectoryName(path)!;
            var newPath = Path.Combine(dir, hideExt ? name + ext : name);
            bool caseOnly = string.Equals(newPath, path, StringComparison.OrdinalIgnoreCase);
            if (!caseOnly && (File.Exists(newPath) || Directory.Exists(newPath)))
            {
                MessageBox.Show($"There's already an item called “{Path.GetFileName(newPath)}” there.",
                                "Rename", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Update the fence first, so the file watcher sees a path we already own.
            bool owned = IsRealIcon && ReplaceItemPath(path, newPath);
            try
            {
                if (isDir) Directory.Move(path, newPath);
                else File.Move(path, newPath);
            }
            catch (Exception ex)
            {
                if (owned) ReplaceItemPath(newPath, path);
                MessageBox.Show("The item couldn't be renamed:\n" + ex.Message, "Rename",
                                MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!IsRealIcon) ReloadItems();
        }

        private void SelectOnly(FenceItem? item)
        {
            foreach (var i in ItemsSource)
                i.IsSelected = ReferenceEquals(i, item);
        }

        // In a portal, folders open inside the portal (Ctrl+double-click opens Explorer instead).
        private void OpenItem(FenceItem item)
        {
            if (_model.IsPortal && Directory.Exists(item.Path) &&
                (Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control)
            {
                NavigatePortal(item.Path);
                return;
            }
            LaunchPath(item.Path);
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

        public static Action? RequestRemoveSelected;

        /// <summary>Paths of the selected items (used by "Remove from fence").</summary>
        public IEnumerable<string> SelectedPaths => ItemsSource.Where(i => i.IsSelected).Select(i => i.Path).ToList();

        /// <summary>True when "Remove from fence" means something here: a real fence other than
        /// the catch-all (where removed items go) with a selection.</summary>
        public bool CanRemoveSelected => IsRealIcon && !IsCatchAllFence && SelectedCount > 0;

        private bool IsCatchAllFence =>
            string.Equals(_model.Name, MainWindow.CatchAllFenceName, StringComparison.OrdinalIgnoreCase);

        /// <summary>The one delete confirmation. It says plainly that real files are deleted
        /// (to the Recycle Bin), and points at "Remove from fence" when that applies.</summary>
        public static bool ConfirmDelete(int count, bool offerRemove)
        {
            string what = count == 1 ? "the selected item" : $"{count} selected items";
            string msg = $"Move {what} to the Recycle Bin?\n\n" +
                         "This deletes the actual file(s) or folder(s) from your computer, not just from the fence.";
            if (offerRemove)
                msg += "\n\nTo only take items out of a fence, right-click and choose “Remove from fence” instead.";
            return MessageBox.Show(msg, "Delete", MessageBoxButton.YesNo, MessageBoxImage.Warning,
                                   MessageBoxResult.No) == MessageBoxResult.Yes;
        }

        public static void ReportDeleteFailures(IReadOnlyCollection<string> failed)
        {
            if (failed.Count == 0) return;
            var names = string.Join("\n", failed.Take(10).Select(p => "• " + Path.GetFileName(p)));
            if (failed.Count > 10) names += $"\n…and {failed.Count - 10} more";
            MessageBox.Show($"These items couldn't be deleted (they may be in use, need admin rights, " +
                            $"or the delete was cancelled):\n\n{names}",
                            "Delete", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        // Sends this fence's selected items to the Recycle Bin without prompting (caller
        // confirms). Returns the paths that couldn't be deleted.
        public List<string> DeleteSelectedItemsNoConfirm()
        {
            var failed = new List<string>();
            var selected = ItemsSource.Where(i => i.IsSelected).ToList();
            if (selected.Count == 0) return failed;

            SetWatcherEnabled(false);
            try
            {
                foreach (var item in selected)
                {
                    // Special (CLSID) items have no file to delete — just release them.
                    bool gone = item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                                || OpenFences.Services.RecycleBin.Send(item.Path);
                    if (!gone) { failed.Add(item.Path); continue; }

                    // Real fences track ownership by path; drop it so it doesn't linger.
                    _model.ItemPaths.RemoveAll(p => string.Equals(p, item.Path, StringComparison.OrdinalIgnoreCase));
                    ItemsSource.Remove(item);
                }
                if (IsRealIcon) Changed?.Invoke(this, EventArgs.Empty);
            }
            finally { SetWatcherEnabled(true); }
            return failed;
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
            if (!ConfirmDelete(n, offerRemove: CanRemoveSelected)) return;
            ReportDeleteFailures(DeleteSelectedItemsNoConfirm());
        }

        // Item menu: "Remove from fence" only applies to real fences other than the catch-all.
        private void ItemMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            foreach (var obj in menu.Items)
                if (obj is MenuItem mi && Equals(mi.Tag, "RemoveFromFence"))
                    mi.Visibility = IsRealIcon && !IsCatchAllFence ? Visibility.Visible : Visibility.Collapsed;
        }

        private void Item_RemoveFromFence_Click(object sender, RoutedEventArgs e)
        {
            if (MenuSenderToItem(sender) is FenceItem item && !item.IsSelected) SelectOnly(item);
            RequestRemoveSelected?.Invoke();
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

        // ---------- Keyboard navigation ----------
        // The item the arrow keys move from (last clicked or keyed to).
        private int _cursorIndex = -1;

        // ScrollViewer consumes arrow keys in its own KeyDown, so navigation runs in Preview.
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            var mods = Keyboard.Modifiers;
            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            switch (key)
            {
                case Key.F2:
                    // F2 renames the selected item; with nothing (or several) selected, the fence.
                    var sel = ItemsSource.Where(i => i.IsSelected).ToList();
                    if (sel.Count == 1) RenameItem(sel[0]);
                    else Rename_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    return;
                case Key.Escape:
                    SelectOnly(null);
                    e.Handled = true;
                    return;
                case Key.Back:
                case Key.Left when mods == ModifierKeys.Alt:
                    if (IsInSubfolder) { PortalUp(); e.Handled = true; }
                    return;
            }

            if (ItemsSource.Count == 0 || (mods & (ModifierKeys.Control | ModifierKeys.Alt)) != 0) return;

            int count = ItemsSource.Count;
            int cur = _cursorIndex >= 0 && _cursorIndex < count ? _cursorIndex : -1;
            int cols = Math.Max(1, (int)(Scroller.ViewportWidth / TileWidth));
            int next = key switch
            {
                Key.Right => cur < 0 ? 0 : Math.Min(count - 1, cur + 1),
                Key.Left => cur < 0 ? 0 : Math.Max(0, cur - 1),
                Key.Down => cur < 0 ? 0 : Math.Min(count - 1, cur + cols),
                Key.Up => cur < 0 ? 0 : Math.Max(0, cur - cols),
                Key.Home => 0,
                Key.End => count - 1,
                _ => NextByLetter(key, cur)
            };
            if (next < 0) return;

            // Shift extends the selection; otherwise the keyed-to item becomes the selection.
            if ((mods & ModifierKeys.Shift) == ModifierKeys.Shift) ItemsSource[next].IsSelected = true;
            else SelectOnly(ItemsSource[next]);

            _cursorIndex = next;
            (Items.ItemContainerGenerator.ContainerFromIndex(next) as FrameworkElement)?.BringIntoView();
            e.Handled = true;
        }

        // Typing a letter or digit jumps to the next item whose name starts with it.
        private int NextByLetter(Key key, int cur)
        {
            char c;
            if (key >= Key.A && key <= Key.Z) c = (char)('a' + (key - Key.A));
            else if (key >= Key.D0 && key <= Key.D9) c = (char)('0' + (key - Key.D0));
            else return -1;

            int count = ItemsSource.Count;
            for (int step = 1; step <= count; step++)
            {
                int i = (Math.Max(cur, -1) + step) % count;
                var name = ItemsSource[i].DisplayName;
                if (name.Length > 0 && char.ToLowerInvariant(name[0]) == c) return i;
            }
            return -1;
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
            if (_model.Collapsed) TempExpand(true); // let the user drop into a rolled-up fence
            FenceWindow_DragOver(sender, e);
        }

        private void FenceWindow_DragOver(object sender, System.Windows.DragEventArgs e)
        {
            // Tiles from a fence move (to this fence, or within it); files from elsewhere copy.
            if (e.Data.GetDataPresent(ItemPathsFormat) && IsRealIcon)
                e.Effects = System.Windows.DragDropEffects.Move;
            else if (e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop))
                e.Effects = System.Windows.DragDropEffects.Copy;
            else
                e.Effects = System.Windows.DragDropEffects.None;
            e.Handled = true;
        }

        private void FenceWindow_Drop(object sender, System.Windows.DragEventArgs e)
        {
            // Tiles dragged from a normal fence: reorder within the same fence, or take them over.
            // (Portal tiles are files from elsewhere; they go through the file-drop path below.)
            if (IsRealIcon && _dragSource is { IsPortal: false } &&
                e.Data.GetData(ItemPathsFormat) is string[] fencePaths && fencePaths.Length > 0)
            {
                e.Handled = true;
                if (ReferenceEquals(_dragSource, this))
                    ReorderItems(fencePaths, FindItem(e.OriginalSource as DependencyObject));
                else
                    AssignToThisFence(fencePaths);
                return;
            }

            if (!e.Data.GetDataPresent(System.Windows.DataFormats.FileDrop)) return;
            var paths = (string[])e.Data.GetData(System.Windows.DataFormats.FileDrop)!;

            if (!IsRealIcon)
            {
                // A single folder dropped on a portal: offer to show that folder here.
                if (paths.Length == 1 && Directory.Exists(paths[0]))
                {
                    PromptPortalFolderDrop(paths[0]);
                    return;
                }

                // Files: copy them into the folder being shown. Dropped folders are skipped
                // for safety (no recursive copies by accident).
                foreach (var p in paths)
                {
                    try
                    {
                        if (Directory.Exists(p)) continue;
                        var dest = Path.Combine(_currentFolder, Path.GetFileName(p));
                        if (!File.Exists(dest)) File.Copy(p, dest);
                    }
                    catch { /* ignore */ }
                }
                ReloadItems();
                return;
            }

            // Real fence: own the ACTUAL desktop items and render them as tiles. Items already
            // on the desktop are assigned as-is (moved out of whatever fence held them). Items
            // from elsewhere aren't desktop items yet, so we ask how to bring them onto the
            // desktop first (move the real file, or drop a shortcut).
            var onDesktop = new List<string>();  // desktop paths ready to assign now
            var offDesktop = new List<string>(); // source paths needing the user's decision
            foreach (var p in paths)
            {
                if (OpenFences.Services.DesktopItems.IsOnDesktop(p)) onDesktop.Add(p);
                else offDesktop.Add(p);
            }

            if (onDesktop.Count > 0) AssignToThisFence(onDesktop);

            if (offDesktop.Count > 0)
                PromptOffDesktopDrop(offDesktop);
        }

        // Route an assignment through MainWindow so the items leave any other fence and get
        // persisted; fall back to a local add if the app hasn't wired the callback.
        private void AssignToThisFence(IReadOnlyList<string> desktopPaths)
        {
            if (RequestAssignItems != null) RequestAssignItems(this, desktopPaths);
            else AddItems(desktopPaths);
        }

        private ContextMenu? _dropMenu;

        private void PromptPortalFolderDrop(string folder)
        {
            var menu = _dropMenu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };
            var show = new MenuItem { Header = $"Show “{new DirectoryInfo(folder).Name}” in this portal" };
            show.Click += (_, __) => RetargetPortal(folder);
            menu.Items.Add(show);
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Cancel" });
            menu.IsOpen = true;
        }

        /// <summary>Ask (via a menu at the drop point) how to bring off-desktop items onto the
        /// desktop, then assign the resulting real desktop items to this fence.</summary>
        private void PromptOffDesktopDrop(List<string> sources)
        {
            string userDesktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            string what = sources.Count == 1 ? $"\"{Path.GetFileName(sources[0])}\"" : $"{sources.Count} items";

            var menu = _dropMenu = new ContextMenu
            {
                Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint
            };

            var move = new MenuItem { Header = $"Move {what} to the desktop" };
            move.Click += (_, __) => CompleteOffDesktopDrop(sources, userDesktop, asShortcut: false);

            var link = new MenuItem { Header = "Create a shortcut on the desktop" };
            link.Click += (_, __) => CompleteOffDesktopDrop(sources, userDesktop, asShortcut: true);

            menu.Items.Add(move);
            menu.Items.Add(link);
            menu.Items.Add(new Separator());
            menu.Items.Add(new MenuItem { Header = "Cancel" });
            menu.IsOpen = true;
        }

        private void CompleteOffDesktopDrop(List<string> sources, string userDesktop, bool asShortcut)
        {
            var assigned = new List<string>();
            foreach (var src in sources)
            {
                try
                {
                    if (asShortcut)
                    {
                        // Reference the item in place with a .lnk on the real desktop.
                        var linkPath = Path.Combine(userDesktop, $"{Path.GetFileNameWithoutExtension(src)}.lnk");
                        ShellLink.CreateShortcut(linkPath, src);
                        assigned.Add(linkPath);
                    }
                    else
                    {
                        // Physically move the real file onto the desktop.
                        if (Directory.Exists(src)) continue; // don't relocate whole folders
                        var dest = Path.Combine(userDesktop, Path.GetFileName(src));
                        if (!File.Exists(dest)) File.Move(src, dest);
                        assigned.Add(dest);
                    }
                }
                catch { /* ignore */ }
            }

            if (assigned.Count > 0) AssignToThisFence(assigned);
        }

        // ---------- Context menu actions ----------

        // The ✎ button opens the full management menu (rename, sort, icon size,
        // transparency, delete) so everything is reachable without right-clicking.
        private void Edit_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || FenceMenu is null) return;
            RefreshMenuChecks();
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

                // Renaming only changes the fence's display label. Portals never touch the
                // mirrored folder, and real-icon fences have no backing folder at all.
                _model.Name = newName;
                TitleText.Text = _model.IsPortal ? "🗁 " + _model.Name : _model.Name;
                FenceRenamed?.Invoke(this, EventArgs.Empty);
            }
        }

        private void OpenFolder_Click(object sender, RoutedEventArgs e)
        {
            // Portals open the folder they're showing; real-icon fences open the Desktop.
            string folder = _model.IsPortal
                ? _currentFolder
                : Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            try
            {
                Process.Start(new ProcessStartInfo { FileName = folder, UseShellExecute = true });
            }
            catch { /* ignore */ }
        }

        private void AddSystemShortcuts_Click(object sender, RoutedEventArgs e)
        {
            if (IsRealIcon)
            {
                // Assign the well-known special desktop items (This PC, Recycle Bin, …) to this
                // fence as tiles. They render straight from their shell:::{CLSID} monikers.
                var monikers = new[]
                {
                    "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // This PC
                    "shell:::{5399E694-6CE5-4D6C-8FCE-1D8870FDCBA0}", // Control Panel
                    "shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", // Network
                    "shell:::{645FF040-5081-101B-9F08-00AA002F954E}", // Recycle Bin
                };
                AssignToThisFence(monikers);
                MessageBox.Show("Added system items to this fence.",
                    "OpenFences", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

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
            if (sender is MenuItem mi && double.TryParse(Convert.ToString(mi.Tag), System.Globalization.NumberStyles.Float,
                                                         System.Globalization.CultureInfo.InvariantCulture, out double alpha))
            {
                _model.BackgroundOpacity = Math.Clamp(alpha, 0.0, 1.0);
                ApplyBackground();
                Changed?.Invoke(this, EventArgs.Empty);  // persist transparency
            }
        }

        private void TitleBar_ContextMenuOpening(object sender, ContextMenuEventArgs e) => RefreshMenuChecks();

        // Tick the active option in each submenu.
        private void RefreshMenuChecks()
        {
            var inv = System.Globalization.CultureInfo.InvariantCulture;
            CheckByTag(SortMenu, _model.Sort.ToString());
            CheckByTag(IconSizeMenu, _model.IconSize.ToString());
            CheckByTag(ColorMenu, _model.AccentColor ?? "");
            CheckByTag(TitleSizeMenu, _model.TitleFontSize.ToString(inv));
            CheckByTag(TransparencyMenu, _model.BackgroundOpacity.ToString("0.00", inv));
            MiChangeFolder.Visibility = _model.IsPortal ? Visibility.Visible : Visibility.Collapsed;
            MiLock.IsChecked = _model.Locked;
            bool hasMedia = !string.IsNullOrWhiteSpace(_model.BackgroundMedia);
            MiRemoveBackground.IsEnabled = BgFitMenu.IsEnabled = BgDimMenu.IsEnabled = hasMedia;
            MiMuteBackground.IsEnabled = hasMedia && IsVideo(_model.BackgroundMedia!);
            MiMuteBackground.IsChecked = _model.BackgroundMuted;
            CheckByTag(BgFitMenu, _model.BackgroundFit.ToString());
            CheckByTag(BgDimMenu, _model.BackgroundDim.ToString("0.00", inv));
            MiGlass.IsChecked = _model.Glass;
        }

        private void Color_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not MenuItem mi) return;
            var tag = Convert.ToString(mi.Tag);
            _model.AccentColor = string.IsNullOrEmpty(tag) ? null : tag;
            ApplyBackground();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void CustomColor_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new System.Windows.Forms.ColorDialog { FullOpen = true, AnyColor = true };
            if (TryParseColor(_model.AccentColor, out var current))
                dlg.Color = System.Drawing.Color.FromArgb(current.R, current.G, current.B);
            if (dlg.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;

            _model.AccentColor = $"#{dlg.Color.R:X2}{dlg.Color.G:X2}{dlg.Color.B:X2}";
            ApplyBackground();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void TitleSize_Click(object sender, RoutedEventArgs e)
        {
            if (sender is MenuItem mi &&
                double.TryParse(Convert.ToString(mi.Tag), System.Globalization.NumberStyles.Float,
                                System.Globalization.CultureInfo.InvariantCulture, out var size))
            {
                _model.TitleFontSize = size;
                ApplyBackground();
                Changed?.Invoke(this, EventArgs.Empty);
            }
        }

        private static void CheckByTag(MenuItem? parent, string activeTag)
        {
            if (parent is null) return;
            foreach (var obj in parent.Items)
                if (obj is MenuItem mi)
                    mi.IsChecked = string.Equals(Convert.ToString(mi.Tag), activeTag, StringComparison.Ordinal);
        }

        // ✕ closes the fence but keeps it (and its items); it stays closed until reopened.
        private void Close_Click(object sender, RoutedEventArgs e) => CloseFence();

        public void CloseFence()
        {
            _model.Closed = true;
            Hide();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>True when the user closed this fence with ✕ (it's kept, just not shown).</summary>
        public bool IsClosedByUser => _model.Closed;

        /// <summary>Show a fence the user had closed.</summary>
        public void Reopen()
        {
            _model.Closed = false;
            Show();
            EnsureBottomZOrder();
            Changed?.Invoke(this, EventArgs.Empty);
        }

        private void DeleteFence_Click(object sender, RoutedEventArgs e)
        {
            // MainWindow owns the single confirm + folder handling (portal-aware).
            DeleteRequested?.Invoke(this, EventArgs.Empty);
        }
    }
}
