// Records the README and website demos: real fence windows play a scripted sequence on screen
// (top-left of the main monitor) while ffmpeg records that area.
//  - pickets-tour: fences, tabs, roll-up, search, Quick Look, the system monitor, backgrounds,
//    light and dark (Tour.cs)
//  - backgrounds-demo: fence picture and video backgrounds (below)
// All media is generated here (a drawn landscape, an ffmpeg animation, made-up monitor
// readings), so nothing personal or copyrighted ends up in the docs.
//
// Usage: dotnet run --project tools/DemoVideo -- <output dir> [path to ffmpeg.exe] [--tour | --backgrounds]
// Writes <name>.mp4 and <name>.gif for both clips (or just the one asked for). Leave the top-left
// of the main screen visible and don't touch the mouse or keyboard while it records (about a minute).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Path = System.IO.Path;
using Rectangle = System.Windows.Shapes.Rectangle;
using System.Windows.Threading;
using Pickets;

internal static class Program
{
    internal const int SceneX = 40, SceneY = 40, SceneW = 1280, SceneH = 720;
    private const double Seconds = 17;

    internal static string _out = ".";
    private static string _ffmpeg = "ffmpeg";
    internal static string _work = "";
    private static Window? _scene;
    private static TextBlock? _caption;
    internal static readonly List<FenceWindow> _fences = new();
    // Windows shown over the fences (search, Quick Look): kept above them while recording.
    internal static readonly List<Window> _overlays = new();

    [STAThread]
    private static void Main(string[] args)
    {
        var positional = args.Where(a => !a.StartsWith("--")).ToList();
        _out = Path.GetFullPath(positional.Count > 0 ? positional[0] : ".");
        if (positional.Count > 1) _ffmpeg = positional[1];
        bool tour = !args.Contains("--backgrounds"), backgrounds = !args.Contains("--tour");
        Directory.CreateDirectory(_out);
        _work = Path.Combine(Path.GetTempPath(), "PicketsDemo");
        if (Directory.Exists(_work)) Directory.Delete(_work, true);
        Directory.CreateDirectory(_work);

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        // Same order as App.xaml: the color palette first, then the menu styles that use it.
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Pickets;component/Themes/Palette.Dark.xaml")
        });
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Pickets;component/Themes/DarkMenu.xaml")
        });
        // One clip after the other; each cleans up its windows before the next starts.
        Action finish = () => System.Windows.Application.Current.Shutdown();
        Action next = tour ? () => Tour.Run(finish) : finish;
        app.Dispatcher.BeginInvoke(backgrounds ? () => Run(next) : next);
        app.Run();
    }

    private static void Run(Action then)
    {
        var picture = DrawLandscape(Path.Combine(_work, "landscape.png"));
        var video = Path.Combine(_work, "aurora.mp4");
        Ffmpeg($"-y -f lavfi -i \"gradients=s=960x540:c0=0x1e3a8a:c1=0x7c3aed:c2=0xdb2777:c3=0x0ea5e9:n=4:speed=0.015:duration=20:rate=30\" " +
               $"-c:v libx264 -pix_fmt yuv420p \"{video}\"");

        ShowScene();
        var items = DemoItems();
        var apps = AddFence(new FenceModel { Name = "Apps", ItemPaths = items.apps, Width = 400, Height = 250 }, 220, 70);
        var photos = AddFence(new FenceModel { Name = "Photos", ItemPaths = items.photos, Width = 400, Height = 250 }, 660, 70);
        var media = AddFence(new FenceModel { Name = "Media", ItemPaths = items.media, Width = 400, Height = 250, AccentColor = "#8B5CF6" }, 660, 370);
        Pin();

        // Scripted timeline (seconds from the start of recording).
        var steps = new List<(double at, Action act)>
        {
            (0.0, () => Caption("Give any fence a picture background…")),
            (1.2, () => Set(photos, m => { m.BackgroundMedia = picture; m.BackgroundDim = 0.2; })),
            (3.4, () => Caption("…or a looping video")),
            (4.4, () => Set(media, m => { m.BackgroundMedia = video; m.BackgroundDim = 0.2; })),
            (6.6, () => Caption("Backgrounds follow the fence's transparency")),
            (12.0, () => Caption("Darken them to keep labels readable")),
            (12.8, () => Set(photos, m => m.BackgroundDim = 0.55)),
            (13.6, () => Set(photos, m => m.BackgroundDim = 0.0)),
            (14.4, () => Set(photos, m => m.BackgroundDim = 0.2)),
            (15.0, () => Caption("Fill, Fit or Stretch")),
            (15.6, () => Set(photos, m => m.BackgroundFit = FenceBackgroundFit.Fit)),
            (16.3, () => Set(photos, m => m.BackgroundFit = FenceBackgroundFit.Fill)),
        };
        // Fade both backgrounds down and back up, between 7.4s and 11.4s.
        for (int i = 0; i <= 40; i++)
        {
            double t = i / 40.0;
            double opacity = 0.92 - 0.62 * Math.Sin(Math.PI * t); // 0.92 → 0.30 → 0.92
            steps.Add((7.4 + 4 * t, () => { Set(photos, m => m.BackgroundOpacity = opacity); Set(media, m => m.BackgroundOpacity = opacity); }));
        }

        // Let the video and icons load before recording starts.
        After(2.5, () =>
        {
            var rec = StartRecording("backgrounds-demo", Seconds);
            var clock = Stopwatch.StartNew();
            var pending = steps.OrderBy(s => s.at).ToList();
            var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            tick.Tick += (_, __) =>
            {
                double now = clock.Elapsed.TotalSeconds;
                while (pending.Count > 0 && pending[0].at <= now) { pending[0].act(); pending.RemoveAt(0); }
                Pin();
                if (now < Seconds + 0.8) return;

                tick.Stop();
                rec.WaitForExit(15000);
                CloseScene();
                MakeGif("backgrounds-demo", width: 800, fps: 12);
                Console.WriteLine("Saved " + Path.Combine(_out, "backgrounds-demo.mp4") + " and .gif");
                then();
            };
            tick.Start();
        });
    }

    // ---------- Scene ----------

    internal static void CloseScene()
    {
        foreach (var w in _overlays) w.Close();
        foreach (var f in _fences) f.Close();
        _overlays.Clear();
        _fences.Clear();
        _scene?.Close();
        _scene = null;
    }

    internal static void ShowScene()
    {
        _caption = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 22,
            FontWeight = FontWeights.SemiBold,
            Padding = new Thickness(18, 8, 18, 8)
        };
        var pill = new Border
        {
            Child = _caption,
            Background = new SolidColorBrush(Color.FromArgb(170, 10, 12, 20)),
            CornerRadius = new CornerRadius(20),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Bottom,
            Margin = new Thickness(0, 0, 0, 28)
        };

        // Bright, busy wallpaper so transparency is easy to see.
        var wallpaper = new Grid
        {
            Background = new LinearGradientBrush(Color.FromRgb(0xF5, 0x9E, 0x0B), Color.FromRgb(0x06, 0xB6, 0xD4), 30)
        };
        var stripes = new Canvas();
        for (int i = 0; i < 30; i++)
        {
            var r = new Rectangle { Width = 28, Height = SceneH * 2, Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)) };
            Canvas.SetLeft(r, i * 70 - 200);
            r.RenderTransform = new RotateTransform(20);
            stripes.Children.Add(r);
        }
        wallpaper.Children.Add(stripes);
        wallpaper.Children.Add(pill);

        _scene = new Window
        {
            WindowStyle = WindowStyle.None,
            ResizeMode = ResizeMode.NoResize,
            ShowInTaskbar = false,
            ShowActivated = false,
            Topmost = true,
            Left = SceneX,
            Top = SceneY,
            Width = SceneW,
            Height = SceneH,
            Content = wallpaper
        };
        _scene.Show();
    }

    internal static FenceWindow AddFence(FenceModel m, double x, double y)
    {
        // A fence places itself where its model says once its window exists, so the model
        // carries the position (setting Left/Top alone gets overridden).
        m.Left = SceneX + x;
        m.Top = SceneY + y;
        var w = new FenceWindow(m) { ShowActivated = false, Topmost = true };
        w.Left = m.Left;
        w.Top = m.Top;
        w.Show();
        _fences.Add(w);
        return w;
    }

    // Fences put themselves in the desktop layer; keep them above the scene for the recording,
    // and search or Quick Look above them.
    internal static void Pin()
    {
        foreach (var w in _fences.Cast<Window>().Concat(_overlays).Where(w => w.IsVisible))
            SetWindowPos(new WindowInteropHelper(w).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
    }

    /// <summary>Put a window (in screen pixels within the scene) on top of the fences.</summary>
    internal static void ShowOverlay(Window w, double x, double y, double width = double.NaN, double height = double.NaN)
    {
        w.Topmost = true;
        w.Show();
        // Windows that size themselves to the monitor (Quick Look) get the scene's size instead.
        if (!double.IsNaN(width)) w.Width = width;
        if (!double.IsNaN(height)) w.Height = height;
        w.Left = SceneX + x;
        w.Top = SceneY + y;
        _overlays.Add(w);
        Pin();
    }

    private static readonly MethodInfo ApplyBackground =
        typeof(FenceWindow).GetMethod("ApplyBackground", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ModelField =
        typeof(FenceWindow).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!;

    internal static void Set(FenceWindow w, Action<FenceModel> change)
    {
        change((FenceModel)ModelField.GetValue(w)!);
        ApplyBackground.Invoke(w, null);
    }

    internal static void Caption(string text) { if (_caption != null) _caption.Text = text; }

    // ---------- Demo content ----------

    private static (List<string> apps, List<string> photos, List<string> media) DemoItems()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sys = Environment.SystemDirectory;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var apps = new List<string>();
        foreach (var (name, target) in new[]
                 {
                     ("Microsoft Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe")),
                     ("File Explorer", Path.Combine(win, "explorer.exe")),
                     ("Notepad", Path.Combine(sys, "notepad.exe")),
                     ("Calculator", Path.Combine(sys, "calc.exe")),
                     ("Task Manager", Path.Combine(sys, "Taskmgr.exe")),
                 })
        {
            if (!File.Exists(target)) continue;
            var link = Path.Combine(_work, name + ".lnk");
            dynamic l = shell.CreateShortcut(link);
            l.TargetPath = target;
            l.Save();
            apps.Add(link);
        }

        List<string> Files(params string[] names) => names.Select(n =>
        {
            var p = Path.Combine(_work, n);
            File.WriteAllText(p, "");
            return p;
        }).ToList();

        return (apps,
                Files("Beach Trip.jpg", "Birthday.png", "Mountains.jpg", "Family.jpg", "Sunset.png"),
                Files("Vacation.mp4", "Playlist.m3u", "Podcast.mp3", "Trailer.mp4"));
    }

    // A simple sunset landscape (sky, sun, layered hills) drawn here, so it's free to publish.
    internal static string DrawLandscape(string path)
    {
        const int w = 1600, h = 900;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.DrawRectangle(new LinearGradientBrush(
                new GradientStopCollection
                {
                    new(Color.FromRgb(0x1E, 0x1B, 0x4B), 0.0),
                    new(Color.FromRgb(0x7C, 0x3A, 0xED), 0.45),
                    new(Color.FromRgb(0xF9, 0x73, 0x16), 0.8),
                    new(Color.FromRgb(0xFD, 0xE0, 0x47), 1.0),
                }, 90), null, new Rect(0, 0, w, h));
            dc.DrawEllipse(new RadialGradientBrush(Color.FromRgb(0xFF, 0xF7, 0xC2), Color.FromArgb(0, 0xFD, 0xBA, 0x74)),
                           null, new Point(w * 0.62, h * 0.66), 260, 260);

            var hills = new[] { (0.62, "#4C1D95"), (0.72, "#312E81"), (0.82, "#1E1B4B"), (0.9, "#0F0A2E") };
            var rnd = new Random(7);
            foreach (var (baseline, color) in hills)
            {
                var g = new StreamGeometry();
                using (var ctx = g.Open())
                {
                    ctx.BeginFigure(new Point(0, h), true, true);
                    for (int x = 0; x <= w; x += 80)
                        ctx.LineTo(new Point(x, h * baseline - 60 * Math.Sin(x / 260.0 + baseline * 9) - rnd.Next(0, 30)), true, true);
                    ctx.LineTo(new Point(w, h), true, true);
                }
                dc.DrawGeometry((Brush)new BrushConverter().ConvertFromString(color)!, null, g);
            }
        }
        var bmp = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(dv);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using (var fs = File.Create(path)) enc.Save(fs);
        return path;
    }

    // ---------- Recording ----------

    internal static Process StartRecording(string name, double seconds)
    {
        var mp4 = Path.Combine(_out, name + ".mp4");
        // The scene is laid out in WPF units; on a scaled display (125%, 150%) it covers more screen
        // pixels, so record that area and scale it back, and every PC makes the same 1280x720 clip.
        static int Px(double units, bool even = false)
        {
            int px = (int)Math.Round(units * Scale);
            return even ? px / 2 * 2 : px; // x264 needs an even size
        }
        // gdigrab captures layered (transparent) windows like the fences; no mouse pointer.
        return Process.Start(new ProcessStartInfo(_ffmpeg,
            $"-y -hide_banner -loglevel error -f gdigrab -framerate 30 -draw_mouse 0 " +
            $"-offset_x {Px(SceneX)} -offset_y {Px(SceneY)} -video_size {Px(SceneW, true)}x{Px(SceneH, true)} -t {seconds} -i desktop " +
            $"-vf scale={SceneW}:{SceneH}:flags=lanczos " +
            $"-c:v libx264 -preset slow -crf 24 -pix_fmt yuv420p -movflags +faststart \"{mp4}\"")
        { UseShellExecute = false, CreateNoWindow = true })!;
    }

    // README-friendly GIF with a palette made for the clip.
    internal static void MakeGif(string name, int width, int fps)
    {
        var mp4 = Path.Combine(_out, name + ".mp4");
        var gif = Path.Combine(_out, name + ".gif");
        Ffmpeg($"-y -i \"{mp4}\" -vf \"fps={fps},scale={width}:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle\" \"{gif}\"");
    }

    internal static void Ffmpeg(string args)
    {
        using var p = Process.Start(new ProcessStartInfo(_ffmpeg, "-hide_banner -loglevel error " + args)
        { UseShellExecute = false, CreateNoWindow = true })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed: " + args);
    }

    // Screen pixels per WPF unit. Everything in the scene (windows, fences, offsets) is in WPF units;
    // only the recording works in screen pixels.
    internal static double Scale
    {
        get
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
    }

    internal static void After(double seconds, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, __) => { t.Stop(); action(); };
        t.Start();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
