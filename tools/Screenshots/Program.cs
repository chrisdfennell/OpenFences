// Renders the README screenshots from the real OpenFences UI, filled with neutral demo content
// (built-in Windows apps and made-up documents in a temp folder), so nobody's own desktop ends
// up in the docs. Usage: dotnet run --project tools/Screenshots -- OpenFences/Docs
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using OpenFences;

internal static class Program
{
    private const double Dpi = 144; // render at 1.5x for crisp images
    private static string _outDir = ".";
    private static string _demo = "";

    [STAThread]
    private static void Main(string[] args)
    {
        _outDir = Path.GetFullPath(args.Length > 0 ? args[0] : ".");
        Directory.CreateDirectory(_outDir);

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
        try
        {
            _demo = CreateDemoFolder();
            var fences = BuildFences();

            // Icons load on a background thread; give them time to arrive.
            After(TimeSpan.FromSeconds(4), () =>
            {
                SaveDesktop(fences, "screenshot-desktop.png", search: null);
                SaveDesktop(fences, "screenshot-search.png", search: "no");
                SaveWelcome("screenshot-welcome.png");
                foreach (var f in fences) f.Close();
                Console.WriteLine("Saved screenshots to " + _outDir);
                System.Windows.Application.Current.Shutdown();
            });
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex);
            System.Windows.Application.Current.Shutdown(1);
        }
    }

    // ---------- Demo content ----------

    private static string CreateDemoFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), "OpenFencesScreenshots");
        if (Directory.Exists(root)) Directory.Delete(root, recursive: true);
        Directory.CreateDirectory(root);

        foreach (var name in new[]
                 {
                     "Quarterly Report.docx", "Budget 2026.xlsx", "Roadmap.pptx", "Meeting Notes.txt",
                     "Invoice-1042.pdf", "Team Photo.jpg", "Design Assets.zip"
                 })
            File.WriteAllText(Path.Combine(root, name), "");

        var projects = Directory.CreateDirectory(Path.Combine(root, "Projects")).FullName;
        foreach (var dir in new[] { "Website Redesign", "Mobile App", "Annual Review" })
            Directory.CreateDirectory(Path.Combine(projects, dir));
        foreach (var file in new[] { "Kickoff Agenda.docx", "Timeline.xlsx", "Notes.md" })
            File.WriteAllText(Path.Combine(projects, file), "");

        // Shortcuts to apps every Windows PC has.
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sys = Environment.SystemDirectory;
        var apps = new (string name, string target)[]
        {
            ("Microsoft Edge", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), @"Microsoft\Edge\Application\msedge.exe")),
            ("File Explorer", Path.Combine(win, "explorer.exe")),
            ("Notepad", Path.Combine(sys, "notepad.exe")),
            ("Calculator", Path.Combine(sys, "calc.exe")),
            ("Command Prompt", Path.Combine(sys, "cmd.exe")),
            ("Task Manager", Path.Combine(sys, "Taskmgr.exe")),
        };
        var appsDir = Directory.CreateDirectory(Path.Combine(root, "Apps")).FullName;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        foreach (var (name, target) in apps.Where(a => File.Exists(a.target)))
        {
            dynamic link = shell.CreateShortcut(Path.Combine(appsDir, name + ".lnk"));
            link.TargetPath = target;
            link.Save();
        }
        return root;
    }

    private static List<FenceWindow> BuildFences()
    {
        string D(string name) => Path.Combine(_demo, name);
        var appLinks = Directory.GetFiles(Path.Combine(_demo, "Apps"), "*.lnk").OrderBy(p => p).ToList();

        var models = new[]
        {
            new FenceModel { Name = "Apps", ItemPaths = appLinks, Left = 60, Top = 60, Width = 430, Height = 290 },
            new FenceModel
            {
                Name = "Documents", AccentColor = "#3B82F6", Left = 530, Top = 60, Width = 520, Height = 290,
                ItemPaths = new[] { "Quarterly Report.docx", "Budget 2026.xlsx", "Roadmap.pptx", "Meeting Notes.txt",
                                    "Invoice-1042.pdf", "Team Photo.jpg", "Design Assets.zip" }.Select(D).ToList()
            },
            new FenceModel
            {
                Name = "System", AccentColor = "#14B8A6", Left = 60, Top = 390, Width = 430, Height = 170,
                ItemPaths = new List<string>
                {
                    "shell:::{20D04FE0-3AEA-1069-A2D8-08002B30309D}", // This PC
                    "shell:::{F02C1A0D-BE21-4350-88B0-7367FC96EF3C}", // Network
                    "shell:::{645FF040-5081-101B-9F08-00AA002F954E}", // Recycle Bin
                }
            },
            new FenceModel
            {
                Name = "Projects", IsPortal = true, FolderPath = D("Projects"), AccentColor = "#8B5CF6",
                Left = 530, Top = 390, Width = 520, Height = 260
            },
            new FenceModel { Name = "Archive", Collapsed = true, Left = 1090, Top = 60, Width = 300, Height = 200 },
        };

        var windows = new List<FenceWindow>();
        foreach (var m in models)
        {
            var w = new FenceWindow(m);
            // Where it goes in the picture. Read before moving off-screen, which updates the model.
            w.Tag = new Point(m.Left, m.Top);
            // Off-screen: rendered to bitmaps, never seen on the real desktop.
            w.Left = -30000 + windows.Count * 2000;
            w.Top = -30000;
            w.Show();
            windows.Add(w);
        }
        return windows;
    }

    // ---------- Rendering ----------

    private const double SceneW = 1440, SceneH = 720;

    private static Canvas Scene()
    {
        var canvas = new Canvas
        {
            Width = SceneW,
            Height = SceneH,
            Background = new LinearGradientBrush(
                new GradientStopCollection
                {
                    new GradientStop(Color.FromRgb(0x1E, 0x3A, 0x8A), 0.0),
                    new GradientStop(Color.FromRgb(0x4C, 0x1D, 0x95), 0.55),
                    new GradientStop(Color.FromRgb(0x0F, 0x17, 0x2A), 1.0),
                },
                new Point(0, 0), new Point(1, 1))
        };
        // A soft glow so the wallpaper isn't a flat gradient.
        var glow = new System.Windows.Shapes.Ellipse
        {
            Width = 900, Height = 900,
            Fill = new RadialGradientBrush(Color.FromArgb(70, 0x60, 0xA5, 0xFA), Color.FromArgb(0, 0x60, 0xA5, 0xFA))
        };
        Canvas.SetLeft(glow, 700);
        Canvas.SetTop(glow, -300);
        canvas.Children.Add(glow);
        return canvas;
    }

    private static void SaveDesktop(List<FenceWindow> fences, string file, string? search)
    {
        foreach (var f in fences) f.ApplySearch(search);
        Flush();

        var scene = Scene();
        foreach (var f in fences)
            Place(scene, Snapshot(f), (Point)f.Tag, f.ActualWidth, f.ActualHeight);

        if (search != null)
        {
            var dlg = new SearchWindow(fences);
            if (dlg.FindName("QueryBox") is TextBox q) { q.Text = search; q.CaretIndex = search.Length; }
            var card = Detach(dlg);
            Place(scene, card, new Point((SceneW - 560) / 2, 150), 560, double.NaN);
            dlg.Close();
        }

        Save(scene, file);
        foreach (var f in fences) f.ApplySearch(null);
    }

    private static void SaveWelcome(string file)
    {
        var dlg = new WelcomeDialog();
        var content = Detach(dlg);
        var card = new Border
        {
            Background = new SolidColorBrush(Color.FromRgb(0x1B, 0x1B, 0x20)),
            CornerRadius = new CornerRadius(12),
            BorderBrush = new SolidColorBrush(Color.FromRgb(0x3B, 0x3B, 0x46)),
            BorderThickness = new Thickness(1),
            Width = 560,
            Child = content,
            Effect = new System.Windows.Media.Effects.DropShadowEffect { BlurRadius = 30, ShadowDepth = 0, Opacity = 0.6 }
        };
        var scene = Scene();
        scene.Width = 760;
        scene.Height = 550;
        Place(scene, card, new Point(100, 60), 560, double.NaN);
        Save(scene, file);
        dlg.Close();
    }

    // Moves a window's content into the scene (for dialogs whose bindings don't need the window).
    private static FrameworkElement Detach(Window w)
    {
        var content = (FrameworkElement)w.Content;
        w.Content = null;
        // Window-level resources (styles) stay reachable from the detached tree.
        foreach (var key in w.Resources.Keys) content.Resources[key] = w.Resources[key];
        foreach (var md in w.Resources.MergedDictionaries) content.Resources.MergedDictionaries.Add(md);
        return content;
    }

    // Renders the window's content element (rendering a transparent, off-screen Window itself
    // comes out empty).
    private static Image Snapshot(Window w)
    {
        var root = (FrameworkElement)w.Content;
        var bmp = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * Dpi / 96),
                                         (int)Math.Ceiling(root.ActualHeight * Dpi / 96), Dpi, Dpi, PixelFormats.Pbgra32);
        bmp.Render(root);
        return new Image { Source = bmp, Width = root.ActualWidth, Height = root.ActualHeight };
    }

    private static void Place(Canvas scene, FrameworkElement e, Point at, double w, double h)
    {
        if (!double.IsNaN(w)) e.Width = w;
        if (!double.IsNaN(h)) e.Height = h;
        Canvas.SetLeft(e, at.X);
        Canvas.SetTop(e, at.Y);
        scene.Children.Add(e);
    }

    private static void Save(Canvas scene, string file)
    {
        scene.Measure(new Size(scene.Width, scene.Height));
        scene.Arrange(new Rect(0, 0, scene.Width, scene.Height));
        scene.UpdateLayout();
        Flush();

        var bmp = new RenderTargetBitmap((int)(scene.Width * Dpi / 96), (int)(scene.Height * Dpi / 96), Dpi, Dpi, PixelFormats.Pbgra32);
        bmp.Render(scene);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var fs = File.Create(Path.Combine(_outDir, file));
        enc.Save(fs);
    }

    // Let pending layout/render/binding work run.
    private static void Flush() =>
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static void After(TimeSpan delay, Action action)
    {
        var t = new DispatcherTimer { Interval = delay };
        t.Tick += (_, __) => { t.Stop(); action(); };
        t.Start();
    }
}
