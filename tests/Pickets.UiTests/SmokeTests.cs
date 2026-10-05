using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Windows.Automation;
using Xunit;

namespace Pickets.UiTests
{
    /// <summary>
    /// Starts the real Pickets.exe in a sandbox (its own settings and desktop folder, the real
    /// desktop left alone; see Services/Sandbox) and checks it through UI Automation, the way a
    /// screen reader sees it: fences show the right items, every Settings switch has a name, and
    /// closing the window quits cleanly with the fences saved.
    /// </summary>
    public class SmokeTests
    {
        [Fact]
        public void The_app_starts_shows_its_fences_and_quits_cleanly()
        {
            var sandbox = Ui.TempFolder();
            var desktop = Path.Combine(sandbox, "Desktop");
            var data = Path.Combine(sandbox, "AppData");
            Directory.CreateDirectory(desktop);
            Directory.CreateDirectory(data);
            var notes = Path.Combine(desktop, "Notes.txt");
            File.WriteAllText(notes, "x");
            File.WriteAllText(Path.Combine(desktop, "Budget.xlsx"), "x");
            // An existing config, so there's no first-run welcome. Budget.xlsx isn't in a fence:
            // Pickets should put it in the Desktop fence.
            File.WriteAllText(Path.Combine(data, "config.json"), JsonSerializer.Serialize(new
            {
                Fences = new[] { new { Name = "Work", ItemPaths = new[] { notes }, Left = 120, Top = 120, Width = 400, Height = 240 } },
                Options = new { CheckForUpdates = false },
            }));

            var start = new ProcessStartInfo(PicketsExe()) { UseShellExecute = false };
            start.Environment["PICKETS_SANDBOX"] = sandbox;
            using var app = Process.Start(start)!;
            try
            {
                var work = WaitForWindow(app.Id, "Work fence");
                var desktopFence = WaitForWindow(app.Id, "Desktop fence");
                var main = WaitForWindow(app.Id, "Pickets");

                Assert.Equal(new[] { "Notes.txt" }, TileNames(work, "Work"));
                Assert.Contains("Budget.xlsx", TileNames(desktopFence, "Desktop"));

                // Settings: every switch is named after its row.
                var settings = main.FindFirst(TreeScope.Descendants, new AndCondition(
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.RadioButton),
                    new PropertyCondition(AutomationElement.NameProperty, "Settings")));
                Assert.NotNull(settings);
                ((SelectionItemPattern)settings.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                var toggles = Retry(() =>
                {
                    var found = main.FindAll(TreeScope.Descendants,
                        new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.CheckBox)).Cast<AutomationElement>().ToList();
                    return found.Count > 5 ? found : null;
                });
                Assert.All(toggles, t => Assert.False(string.IsNullOrWhiteSpace(t.Current.Name), "A Settings switch has no name."));
                Assert.Contains(toggles, t => t.Current.Name == "Keep stacked fences together");

                ((WindowPattern)main.GetCurrentPattern(WindowPattern.Pattern)).Close();
                Assert.True(app.WaitForExit(20000), "Pickets didn't quit after its window was closed.");
                Assert.Equal(0, app.ExitCode);

                var saved = File.ReadAllText(Path.Combine(data, "config.json"));
                Assert.Contains("\"Work\"", saved);
                Assert.Contains("Budget.xlsx", saved);
            }
            finally
            {
                if (!app.HasExited) app.Kill();
                Ui.DeleteTempFolders();
            }
        }

        private static string[] TileNames(AutomationElement fenceWindow, string listName)
        {
            var list = fenceWindow.FindFirst(TreeScope.Descendants, new AndCondition(
                new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.List),
                new PropertyCondition(AutomationElement.NameProperty, listName)));
            Assert.NotNull(list);
            return Retry(() =>
            {
                var names = list.FindAll(TreeScope.Children,
                    new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.ListItem))
                    .Cast<AutomationElement>().Select(e => e.Current.Name).ToArray();
                return names.Length > 0 ? names : null;
            });
        }

        private static AutomationElement WaitForWindow(int processId, string name) =>
            Retry(() => AutomationElement.RootElement.FindFirst(TreeScope.Children, new AndCondition(
                new PropertyCondition(AutomationElement.ProcessIdProperty, processId),
                new PropertyCondition(AutomationElement.NameProperty, name))), timeoutMs: 30000)
            ?? throw new Exception($"No window called “{name}” appeared.");

        private static T Retry<T>(Func<T?> attempt, int timeoutMs = 10000) where T : class
        {
            var until = DateTime.UtcNow.AddMilliseconds(timeoutMs);
            while (true)
            {
                try { if (attempt() is T found) return found; }
                catch (ElementNotAvailableException) { /* the UI changed under us; look again */ }
                if (DateTime.UtcNow > until) throw new TimeoutException("Gave up waiting for the UI.");
                Thread.Sleep(200);
            }
        }

        // The app as built next to these tests (same configuration).
        private static string PicketsExe()
        {
            var here = AppContext.BaseDirectory; // tests\Pickets.UiTests\bin\<Configuration>\net8.0-windows\
            var dir = new DirectoryInfo(here);
            while (dir != null && !File.Exists(Path.Combine(dir.FullName, "Pickets.sln"))) dir = dir.Parent;
            if (dir == null) throw new FileNotFoundException("Couldn't find the repository root from " + here);
            var config = new DirectoryInfo(here.TrimEnd('\\')).Parent!.Name;
            var exe = Path.Combine(dir.FullName, "Pickets", "bin", config, "net8.0-windows", "Pickets.exe");
            if (!File.Exists(exe)) throw new FileNotFoundException("Build Pickets first.", exe);
            return exe;
        }
    }
}
