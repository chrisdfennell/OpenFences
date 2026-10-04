// Records the README demo of fence picture/video backgrounds: real fence windows play a
// scripted sequence on screen (top-left of the main monitor) while ffmpeg records that area.
// All media is generated here (a drawn landscape, an ffmpeg animation), so nothing personal or
// copyrighted ends up in the docs.
//
// Usage: dotnet run --project tools/DemoVideo -- <output dir> [path to ffmpeg.exe]
// Writes backgrounds-demo.mp4 and backgrounds-demo.gif. Leave the top-left of the main screen
// visible while it records (about 20 seconds).
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
using OpenFences;

internal static class Program
{
    private const int SceneX = 40, SceneY = 40, SceneW = 1280, SceneH = 720;
    private const double Seconds = 17;

    private static string _out = ".";
    private static string _ffmpeg = "ffmpeg";
    private static string _work = "";
    private static Window? _scene;
    private static TextBlock? _caption;
    private static readonly List<FenceWindow> _fences = new();

    [STAThread]
    private static void Main(string[] args)
    {
        _out = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        if (args.Length > 1) _ffmpeg = args[1];
        Directory.CreateDirectory(_out);
        _work = Path.Combine(Path.GetTempPath(), "OpenFencesDemo");
        if (Directory.Exists(_work)) Directory.Delete(_work, true);
        Directory.CreateDirectory(_work);

        var app = new System.Windows.Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/OpenFences;component/Themes/DarkMenu.xaml")
        });
        app.Dispatcher.BeginInvoke(Run);
        app.Run();
    }

    private static void Run()
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
            var rec = StartRecording();
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
                foreach (var f in _fences) f.Close();
                _scene?.Close();
                MakeGif();
                Console.WriteLine("Saved " + Path.Combine(_out, "backgrounds-demo.mp4") + " and .gif");
                System.Windows.Application.Current.Shutdown();
            };
            tick.Start();
        });
    }

    // ---------- Scene ----------

    private static void ShowScene()
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
            Left = SceneX / Scale, Top = SceneY / Scale, Width = SceneW / Scale, Height = SceneH / Scale,
            Content = wallpaper
        };
        _scene.Show();
    }

    private static FenceWindow AddFence(FenceModel m, double x, double y)
    {
        var w = new FenceWindow(m) { ShowActivated = false, Topmost = true };
        w.Left = (SceneX + x) / Scale;
        w.Top = (SceneY + y) / Scale;
        w.Show();
        _fences.Add(w);
        return w;
    }

    // Fences put themselves in the desktop layer; keep them above the scene for the recording.
    private static void Pin()
    {
        foreach (var w in _fences)
            SetWindowPos(new WindowInteropHelper(w).Handle, new IntPtr(-1), 0, 0, 0, 0, 0x13);
    }

    private static readonly MethodInfo ApplyBackground =
        typeof(FenceWindow).GetMethod("ApplyBackground", BindingFlags.Instance | BindingFlags.NonPublic)!;
    private static readonly FieldInfo ModelField =
        typeof(FenceWindow).GetField("_model", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void Set(FenceWindow w, Action<FenceModel> change)
    {
        change((FenceModel)ModelField.GetValue(w)!);
        ApplyBackground.Invoke(w, null);
    }

    private static void Caption(string text) { if (_caption != null) _caption.Text = text; }

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
    private static string DrawLandscape(string path)
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

    private static Process StartRecording()
    {
        var mp4 = Path.Combine(_out, "backgrounds-demo.mp4");
        // gdigrab captures layered (transparent) windows like the fences; no mouse pointer.
        return Process.Start(new ProcessStartInfo(_ffmpeg,
            $"-y -hide_banner -loglevel error -f gdigrab -framerate 30 -draw_mouse 0 " +
            $"-offset_x {SceneX} -offset_y {SceneY} -video_size {SceneW}x{SceneH} -t {Seconds} -i desktop " +
            $"-c:v libx264 -preset slow -crf 24 -pix_fmt yuv420p -movflags +faststart \"{mp4}\"")
        { UseShellExecute = false, CreateNoWindow = true })!;
    }

    // README-friendly GIF: 800px wide, 12 fps, with a palette made for this clip.
    private static void MakeGif()
    {
        var mp4 = Path.Combine(_out, "backgrounds-demo.mp4");
        var gif = Path.Combine(_out, "backgrounds-demo.gif");
        Ffmpeg($"-y -i \"{mp4}\" -vf \"fps=12,scale=800:-1:flags=lanczos,split[a][b];[a]palettegen=max_colors=128:stats_mode=diff[p];[b][p]paletteuse=dither=bayer:bayer_scale=4:diff_mode=rectangle\" \"{gif}\"");
    }

    private static void Ffmpeg(string args)
    {
        using var p = Process.Start(new ProcessStartInfo(_ffmpeg, "-hide_banner -loglevel error " + args)
        { UseShellExecute = false, CreateNoWindow = true })!;
        p.WaitForExit();
        if (p.ExitCode != 0) throw new InvalidOperationException("ffmpeg failed: " + args);
    }

    // Screen pixels per WPF unit (the scene is positioned in pixels, windows in WPF units).
    private static double Scale
    {
        get
        {
            using var g = System.Drawing.Graphics.FromHwnd(IntPtr.Zero);
            return g.DpiX / 96.0;
        }
    }

    private static void After(double seconds, Action action)
    {
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(seconds) };
        t.Tick += (_, __) => { t.Stop(); action(); };
        t.Start();
    }

    [DllImport("user32.dll")]
    private static extern bool SetWindowPos(IntPtr h, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
