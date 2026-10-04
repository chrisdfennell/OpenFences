using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Pickets
{
    // Fully-qualify to avoid WinForms ambiguity if present
    public partial class App : System.Windows.Application
    {
        private const string InstanceMutexName = @"Local\Pickets.SingleInstance";
        private const string ShowEventName = @"Local\Pickets.ShowMainWindow";

        private readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "Pickets", "error.log");

        // Held for the app's lifetime so a second launch can tell we're running.
        private readonly Mutex _instanceMutex;
        private readonly bool _isPrimaryInstance;
        private EventWaitHandle? _showEvent;

        public App()
        {
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out _isPrimaryInstance);

            // A second copy only signals the first one and exits (see OnStartup). It must not
            // register the icon-restore handler below, or exiting would un-hide the desktop
            // icons that the running instance deliberately hid.
            if (!_isPrimaryInstance || RestoreIconsOnly) return;

            // First start after the rename from OpenFences: bring the old settings along.
            Pickets.Services.LegacyMigration.Run();

            this.DispatcherUnhandledException += (s, e) =>
            {
                SafeLog("DispatcherUnhandledException", e.Exception);
                System.Windows.MessageBox.Show("Unexpected error (UI thread). Details were logged.\n" + e.Exception.Message,
                    "Pickets", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                e.Handled = true; // keep app alive
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                SafeLog("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            };

            // Safety net: restore desktop icons on ANY CLR shutdown path (clean exit,
            // unhandled crash, Environment.Exit) — not just MainWindow.OnClosed. It's
            // registry-gated and idempotent, so double-calling is harmless. (A hard
            // TerminateProcess/kill can't run managed code; nothing can cover that.)
            AppDomain.CurrentDomain.ProcessExit += (_, __) =>
            {
                try { DesktopHelper.RestoreDesktopIconsOnExit(); } catch { /* ignore */ }
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                SafeLog("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }

        /// <summary>"Pickets.exe --restore-icons": just bring the desktop icons back and quit, for
        /// when Pickets crashed (or won't start) and left them hidden. Also in the Start menu.</summary>
        private static bool RestoreIconsOnly => Environment.GetCommandLineArgs().Skip(1).Any(a =>
            a.Equals("--restore-icons", StringComparison.OrdinalIgnoreCase) ||
            a.Equals("/restore-icons", StringComparison.OrdinalIgnoreCase));

        private void RestoreIconsAndQuit()
        {
            string message;
            var icon = System.Windows.MessageBoxImage.Information;
            if (!_isPrimaryInstance)
                message = "Pickets is running. While it runs, your desktop items show inside fences instead of as " +
                          "desktop icons.\n\nTo get your normal desktop back, exit Pickets from its tray icon.";
            else if (DesktopHelper.ForceShowDesktopIcons())
                message = "Your desktop icons are back.";
            else
            {
                message = "Pickets couldn't bring your desktop icons back by itself.\n\n" +
                          "Right-click an empty spot on the desktop and choose View → Show desktop icons.";
                icon = System.Windows.MessageBoxImage.Warning;
            }
            System.Windows.MessageBox.Show(message, "Pickets", System.Windows.MessageBoxButton.OK, icon);
            Shutdown();
        }

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

            if (RestoreIconsOnly)
            {
                RestoreIconsAndQuit();
                return;
            }

            if (!_isPrimaryInstance)
            {
                // Ask the running instance to show its window, then quit quietly.
                try
                {
                    if (EventWaitHandle.TryOpenExisting(ShowEventName, out var existing))
                        using (existing) existing.Set();
                }
                catch { /* best effort */ }
                Shutdown();
                return;
            }

            var main = new MainWindow();
            MainWindow = main;
            main.Show();

            // Later launches signal this event; bring the controller back from the tray.
            _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
            ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, __) =>
                Dispatcher.BeginInvoke(() => main.RestoreFromTray()), null, Timeout.Infinite, executeOnlyOnce: false);
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            _showEvent?.Dispose();
            if (_isPrimaryInstance)
            {
                try { _instanceMutex.ReleaseMutex(); } catch { /* ignore */ }
            }
            _instanceMutex.Dispose();
            base.OnExit(e);
        }

        /// <summary>Where errors are logged (About → Copy diagnostics includes its end).</summary>
        internal string LogPath => _logPath;

        internal void SafeLog(string tag, Exception? ex)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_logPath)!);
                File.AppendAllText(_logPath,
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {tag}: {ex}\n----------------------------------------\n");
            }
            catch { /* ignore */ }
        }
    }
}
