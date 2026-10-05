// The README / website tour: a quick run through what Pickets does, played by real fence windows
// and recorded by ffmpeg (see Program.cs for the scene, pinning and recording).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Threading;
using Pickets;
using Pickets.Services;
using Path = System.IO.Path;
using static Program;

internal static class Tour
{
    private const double Seconds = 36;

    public static void Run(Action then)
    {
        // ----- Demo content: shortcuts to built-in apps, made-up documents, drawn pictures -----
        var apps = AppLinks();
        string F(string name) { var p = Path.Combine(_work, name); if (!File.Exists(p)) File.WriteAllText(p, ""); return p; }
        var picture = DrawLandscape(Path.Combine(_work, "Mountains.png"));
        File.Copy(picture, Path.Combine(_work, "Sunset.png"), true);
        var video = Path.Combine(_work, "aurora.mp4");
        if (!File.Exists(video))
            Ffmpeg($"-y -f lavfi -i \"gradients=s=960x540:c0=0x1e3a8a:c1=0x7c3aed:c2=0xdb2777:c3=0x0ea5e9:n=4:speed=0.015:duration=20:rate=30\" " +
                   $"-c:v libx264 -pix_fmt yuv420p \"{video}\"");
        var projects = Directory.CreateDirectory(Path.Combine(_work, "Projects")).FullName;
        foreach (var d in new[] { "Website Redesign", "Mobile App", "Annual Review" }) Directory.CreateDirectory(Path.Combine(projects, d));
        foreach (var f in new[] { "Kickoff Agenda.docx", "Timeline.xlsx" }) File.WriteAllText(Path.Combine(projects, f), "");

        var work = new[] { "Quarterly Report.docx", "Budget 2026.xlsx", "Roadmap.pptx", "Meeting Notes.txt", "Invoice-1042.pdf" }.Select(F).ToList();
        var personal = new[] { "Recipes.docx", "Travel Plans.pdf" }.Select(F).ToList();

        // Monitors show made-up readings, never this PC's own.
        SystemMonitor.ShowFixed(History(60, memory: 62));

        ShowScene();
        var appsFence = AddFence(new FenceModel { Name = "Apps", ItemPaths = apps, Width = 360, Height = 236 }, 40, 40);
        var docs = AddFence(new FenceModel
        {
            Name = "Documents",
            AccentColor = "#3B82F6",
            Width = 440,
            Height = 236,
            ItemPaths = work.Concat(personal).ToList(),
            Tabs = new List<FenceTab> { new() { Name = "Work" }, new() { Name = "Personal", ItemPaths = personal } }
        }, 420, 40);
        var portal = AddFence(new FenceModel
        {
            Name = "Projects",
            IsPortal = true,
            FolderPath = projects,
            AccentColor = "#8B5CF6",
            Width = 360,
            Height = 236
        }, 40, 296);
        var photos = AddFence(new FenceModel
        {
            Name = "Photos",
            ItemPaths = new List<string> { picture, Path.Combine(_work, "Sunset.png"), F("Beach Trip.jpg") },
            Width = 440,
            Height = 236
        }, 420, 296);
        var monitor = AddFence(new FenceModel
        {
            Name = "System",
            IsMonitor = true,
            Width = 364, // two tiles across
            Height = 300,
            Tiles = new List<MonitorTileConfig> { new() { Kind = MonitorMetrics.Cpu }, new() { Kind = MonitorMetrics.Memory }, new() { Kind = MonitorMetrics.Network } }
        }, 890, 40);
        var archive = AddFence(new FenceModel
        {
            Name = "Archive",
            ItemPaths = new[] { "Old Invoices.zip", "Taxes 2024.pdf" }.Select(F).ToList(),
            Width = 364,
            Height = 180
        }, 890, 360);
        Pin();
        Theme.Changed += RepaintAll;

        // Readings keep coming while recording, so the graphs move; memory climbs at 22.5 s.
        double clockAt = 0, repinAt = double.MaxValue;
        int sample = 60;
        double MemoryAt(double t) => t < 22.5 ? 62 : Math.Min(94, 62 + (t - 22.5) * 22);

        SearchWindow? search = null;
        QuickLookWindow? quickLook = null;
        var steps = new List<(double at, Action act)>
        {
            (0.0, () => Caption("Your desktop items, sorted into fences")),
            (2.6, () => Caption("Tabs keep a busy fence tidy")),
            (3.4, () => SwitchTab(docs, 1)),
            (5.0, () => SwitchTab(docs, 0)),
            (6.0, () => Caption("Roll a fence up when you don't need it")),
            (6.6, () => archive.ToggleCollapsed()),
            (8.8, () => archive.ToggleCollapsed()),
            (9.8, () => Caption("Ctrl+Alt+F finds anything in any fence")),
            (10.3, () =>
            {
                search = new SearchWindow(_fences);
                ShowOverlay(search, (SceneW - search.Width) / 2, 150);
            }),
            (10.9, () => TypeQuery(search, "q")),
            (11.2, () => TypeQuery(search, "qu")),
            (11.5, () => TypeQuery(search, "qua")),
            (11.8, () => TypeQuery(search, "quar")),
            (13.6, () => { search?.Close(); _overlays.Remove(search!); }),
            (14.0, () => Caption("Space opens a Quick Look preview")),
            (14.4, () =>
            {
                var items = photos.SearchableItems().Select(x => x.Item).ToList();
                quickLook = new QuickLookWindow(items, Math.Max(0, items.FindIndex(i => i.Path == picture)));
                // Well inside the frame, clear of the caption at the bottom.
                ShowOverlay(quickLook, (SceneW - 760) / 2, 60, width: 760, height: 440);
                // The header shows the file's folder: a temp path with the user's name in it.
                if (quickLook.FindName("DetailsText") is TextBlock details)
                    details.Text = System.Text.RegularExpressions.Regex.Replace(details.Text, @" · [A-Za-z]:\\.*$", " · Pictures");
            }),
            (17.2, () => { quickLook?.Close(); _overlays.Remove(quickLook!); }),
            (17.6, () => Caption("A live system monitor, right on your desktop")),
            (19.8, () => Caption("Add the tiles you want: GPU, cores, drives, battery…")),
            (20.4, () => { monitor.MonitorConfig.Insert(2, new MonitorTileConfig { Kind = MonitorMetrics.Gpu }); monitor.ApplyMonitorChanges(); }),
            (22.4, () => Caption("Warning colors when something runs high")),
            (25.2, () => Caption("Picture and video backgrounds")),
            (25.8, () => Set(photos, m => { m.BackgroundMedia = picture; m.BackgroundDim = 0.25; })),
            (27.0, () => Set(portal, m => { m.BackgroundMedia = video; m.BackgroundDim = 0.3; })),
            (29.4, () => Caption("Light or dark, like Windows")),
            (30.0, () => Theme.Apply(AppTheme.Light)),
            (32.6, () => Theme.Apply(AppTheme.Dark)),
            (33.4, () => Caption("Pickets: free and open source")),
        };

        // Let icons, the video and the first readings load before recording starts.
        After(3.0, () =>
        {
            var rec = StartRecording("pickets-tour", Seconds);
            var clock = Stopwatch.StartNew();
            var pending = steps.OrderBy(s => s.at).ToList();
            var tick = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(15) };
            tick.Tick += (_, __) =>
            {
                double now = clock.Elapsed.TotalSeconds;
                // Re-stack only after a step: re-pinning every frame reshuffles the windows and the
                // recording catches fences flashing over the search box.
                bool stepped = false;
                while (pending.Count > 0 && pending[0].at <= now) { pending[0].act(); pending.RemoveAt(0); stepped = true; }
                if (now - clockAt >= 0.5)
                {
                    clockAt = now;
                    SystemMonitor.ShowFixed(new[] { Reading(sample++, MemoryAt(now)) });
                }
                // Once now, and once more shortly after: a fence that was activated (roll-up)
                // sends itself back to the desktop layer a moment later.
                if (stepped) { Pin(); repinAt = now + 0.4; }
                else if (now >= repinAt) { Pin(); repinAt = double.MaxValue; }
                if (now < Seconds + 0.8) return;

                tick.Stop();
                rec.WaitForExit(20000);
                Theme.Changed -= RepaintAll;
                CloseScene();
                MakeGif("pickets-tour", width: 800, fps: 10);
                Console.WriteLine("Saved " + Path.Combine(_out, "pickets-tour.mp4") + " and .gif");
                then();
            };
            tick.Start();
        });
    }

    private static void RepaintAll()
    {
        foreach (var f in _fences) f.ApplyTheme();
    }

    private static readonly MethodInfo SwitchTabMethod =
        typeof(FenceWindow).GetMethod("SwitchTab", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private static void SwitchTab(FenceWindow w, int index) => SwitchTabMethod.Invoke(w, new object[] { index });

    private static void TypeQuery(SearchWindow? search, string text)
    {
        if (search?.FindName("QueryBox") is TextBox box) { box.Text = text; box.CaretIndex = text.Length; }
    }

    // ---------- Made-up readings ----------
    private static IEnumerable<SystemSnapshot> History(int count, double memory) =>
        Enumerable.Range(0, count).Select(i => Reading(i, memory));

    private static SystemSnapshot Reading(int i, double memoryPercent)
    {
        const long GB = 1024L * 1024 * 1024;
        var rnd = new Random(i);
        double t = i / 60.0 * Math.PI * 2;
        double cpu = 24 + 12 * Math.Sin(t * 3) + rnd.NextDouble() * 6;
        return new SystemSnapshot
        {
            CpuPercent = cpu,
            Processors = 12,
            CoreLoads = Enumerable.Range(0, 12).Select(c => Math.Clamp(cpu + 30 * Math.Sin(c * 1.7 + i / 4.0), 2, 98)).ToList(),
            MemoryUsed = (ulong)(memoryPercent / 100 * 16 * GB),
            MemoryTotal = 16UL * (ulong)GB,
            GpuPercent = 38 + 22 * Math.Sin(t * 2 + 1) + rnd.NextDouble() * 4,
            Drives = new[] { new DriveSample("C:", "Windows", 1000 * GB, 412 * GB) },
            NetDownBytesPerSec = (2.4 + 1.4 * Math.Sin(t * 2.5) + rnd.NextDouble()) * 1024 * 1024,
            NetUpBytesPerSec = (280 + 90 * rnd.NextDouble()) * 1024,
            NetName = "Wi-Fi",
            NetAddress = "192.168.1.24",
            Battery = new BatterySample(76, PluggedIn: false, Charging: false, SecondsLeft: 3 * 3600 + 12 * 60),
        };
    }

    // Shortcuts to apps every Windows PC has.
    private static List<string> AppLinks()
    {
        var win = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
        var sys = Environment.SystemDirectory;
        dynamic shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        var links = new List<string>();
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
            links.Add(link);
        }
        return links;
    }
}
