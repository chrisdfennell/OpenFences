using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

using WinForms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Pickets
{
    /// <summary>
    /// Large preview of a fence item (Space on a tile): pictures, video/audio with sound, text
    /// and code, or a big icon with file details. ←/→ step through the fence's items, Enter
    /// opens, Space or Esc closes.
    /// </summary>
    public partial class QuickLookWindow : Window
    {
        private static readonly string[] PictureExt = { ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tif", ".tiff", ".ico", ".heic", ".heif" };
        private static readonly string[] VideoExt = { ".mp4", ".m4v", ".mov", ".wmv", ".avi", ".mkv", ".webm" };
        private static readonly string[] AudioExt = { ".mp3", ".wav", ".wma", ".m4a", ".aac", ".flac", ".ogg" };
        private static readonly string[] TextExt =
        {
            ".txt", ".md", ".log", ".csv", ".json", ".xml", ".yml", ".yaml", ".ini", ".cfg", ".conf", ".toml",
            ".cs", ".js", ".ts", ".py", ".ps1", ".bat", ".cmd", ".sh", ".html", ".htm", ".css", ".sql", ".c", ".cpp", ".h", ".java", ".go", ".rs"
        };
        private const int MaxTextBytes = 256 * 1024;

        private readonly IReadOnlyList<FenceItem> _items;
        private int _index;
        private bool _playing;

        public QuickLookWindow(IReadOnlyList<FenceItem> items, int index)
        {
            InitializeComponent();
            _items = items;
            _index = Math.Clamp(index, 0, Math.Max(0, items.Count - 1));

            PlaceOnCursorMonitor();
            PreviewKeyDown += QuickLook_PreviewKeyDown;
            Closed += (_, __) => StopMedia();
            Loaded += (_, __) => { Activate(); Focus(); };
            ShowItem();
        }

        // About two-thirds of the monitor the mouse is on, centered.
        private void PlaceOnCursorMonitor()
        {
            var area = WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea;
            double s = Pickets.Services.ScreenLayout.Scale;
            Width = Math.Min(1200, area.Width / s * 0.7);
            Height = Math.Min(860, area.Height / s * 0.78);
            Left = area.Left / s + (area.Width / s - Width) / 2;
            Top = area.Top / s + (area.Height / s - Height) / 2;
        }

        private void ShowItem()
        {
            StopMedia();
            foreach (var v in new UIElement[] { PictureView, MediaView, TextView, InfoView }) v.Visibility = Visibility.Collapsed;
            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _items.Count - 1;
            if (_items.Count == 0) return;

            var item = _items[_index];
            var path = item.Path;
            NameText.Text = item.DisplayName;
            HeaderIcon.Source = item.Icon;
            DetailsText.Text = Details(path);

            var ext = Path.GetExtension(path).ToLowerInvariant();
            try
            {
                if (File.Exists(path) && PictureExt.Contains(ext) && ShowPicture(path)) return;
                if (File.Exists(path) && (VideoExt.Contains(ext) || AudioExt.Contains(ext)))
                {
                    ShowMedia(path, audioOnly: AudioExt.Contains(ext));
                    return;
                }
                if (File.Exists(path) && TextExt.Contains(ext) && ShowText(path)) return;
            }
            catch (Exception ex)
            {
                (System.Windows.Application.Current as App)?.SafeLog("Quick Look", ex);
            }
            ShowInfo(item);
        }

        private bool ShowPicture(string path)
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(path);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.DecodePixelWidth = 2400; // plenty for a preview, bounded memory
            bmp.EndInit();
            bmp.Freeze();
            PictureView.Source = bmp;
            PictureView.Visibility = Visibility.Visible;
            return true;
        }

        private void ShowMedia(string path, bool audioOnly)
        {
            MediaView.Visibility = Visibility.Visible;
            AudioGlyph.Visibility = audioOnly ? Visibility.Visible : Visibility.Collapsed;
            Player.Source = new Uri(path);
            Player.Play();
            _playing = true;
            PlayPause.Content = ""; // pause glyph
        }

        private bool ShowText(string path)
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            var buffer = new byte[Math.Min(fs.Length, MaxTextBytes)];
            int read = fs.Read(buffer, 0, buffer.Length);
            if (buffer.Take(read).Contains((byte)0)) return false; // binary after all

            var text = Encoding.UTF8.GetString(buffer, 0, read);
            if (fs.Length > MaxTextBytes) text += "\n\n… (preview shows the first 256 KB)";
            TextView.Text = text;
            TextView.Visibility = Visibility.Visible;
            TextView.ScrollToHome();
            return true;
        }

        // Anything else: the biggest icon/thumbnail Windows has, plus details.
        private void ShowInfo(FenceItem item)
        {
            InfoImage.Source = Pickets.Services.ShellThumbnail.GetLarge(item.Path, 256) ?? item.Icon;
            InfoText.Text = item.Path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)
                ? "Press Enter to open it."
                : "No preview for this kind of file. Press Enter to open it.";
            InfoView.Visibility = Visibility.Visible;
        }

        internal static string Details(string path)
        {
            try
            {
                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return "Windows item";
                if (Directory.Exists(path))
                {
                    var d = new DirectoryInfo(path);
                    return $"Folder · modified {d.LastWriteTime:MMM d, yyyy h:mm tt} · {d.Parent?.FullName}";
                }
                var f = new FileInfo(path);
                return $"{TypeName(f.Extension)} · {Size(f.Length)} · modified {f.LastWriteTime:MMM d, yyyy h:mm tt} · {f.DirectoryName}";
            }
            catch { return path; }
        }

        private static string TypeName(string ext) =>
            string.IsNullOrEmpty(ext) ? "File" : ext.TrimStart('.').ToUpperInvariant() + " file";

        private static string Size(long bytes) =>
            bytes >= 1L << 30 ? $"{bytes / (double)(1L << 30):0.0} GB" :
            bytes >= 1L << 20 ? $"{bytes / (double)(1L << 20):0.0} MB" :
            bytes >= 1L << 10 ? $"{bytes / 1024.0:0} KB" : $"{bytes} bytes";

        // ---------- Media ----------
        private void Player_MediaOpened(object sender, RoutedEventArgs e) { }

        private void Player_MediaEnded(object sender, RoutedEventArgs e)
        {
            Player.Position = TimeSpan.Zero;
            Player.Play();
        }

        private void PlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_playing) Player.Pause(); else Player.Play();
            _playing = !_playing;
            PlayPause.Content = _playing ? "" : ""; // pause / play glyphs
        }

        private void StopMedia()
        {
            try { Player.Stop(); Player.Source = null; } catch { /* ignore */ }
            _playing = false;
        }

        // ---------- Navigation ----------
        private void Step(int delta)
        {
            int next = _index + delta;
            if (next < 0 || next >= _items.Count) return;
            _index = next;
            ShowItem();
        }

        private void QuickLook_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Escape:
                case Key.Space:
                    Close(); e.Handled = true; break;
                case Key.Left:
                case Key.Up:
                    Step(-1); e.Handled = true; break;
                case Key.Right:
                case Key.Down:
                    Step(+1); e.Handled = true; break;
                case Key.Enter:
                    Open_Click(this, new RoutedEventArgs()); e.Handled = true; break;
            }
        }

        private void Prev_Click(object sender, RoutedEventArgs e) => Step(-1);
        private void Next_Click(object sender, RoutedEventArgs e) => Step(+1);
        private void Close_Click(object sender, RoutedEventArgs e) => Close();

        private void Open_Click(object sender, RoutedEventArgs e)
        {
            if (_items.Count == 0) return;
            var path = _items[_index].Path;
            Close();
            FenceWindow.LaunchPath(path);
        }
    }
}
