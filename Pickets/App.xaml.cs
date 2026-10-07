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
        // A sandbox (Services/Sandbox) gets its own names, so it runs next to the real Pickets.
        private static readonly string InstanceMutexName = @"Local\Pickets.SingleInstance" + Pickets.Services.Sandbox.InstanceSuffix;
        private static readonly string ShowEventName = @"Local\Pickets.ShowMainWindow" + Pickets.Services.Sandbox.InstanceSuffix;
        // A later launch asks (Ping) and the running Pickets answers (Pong) from its UI thread.
        private static readonly string PingEventName = @"Local\Pickets.Ping" + Pickets.Services.Sandbox.InstanceSuffix;
        private static readonly string PongEventName = @"Local\Pickets.Pong" + Pickets.Services.Sandbox.InstanceSuffix;

        private readonly string _logPath = Path.Combine(Pickets.Services.Sandbox.DataDir, "error.log");

        // Held for the app's lifetime so a second launch can tell we're running.
        private readonly Mutex _instanceMutex;
        private readonly bool _isPrimaryInstance;
        private EventWaitHandle? _showEvent, _pingEvent, _pongEvent;

        public App()
        {
            _instanceMutex = new Mutex(initiallyOwned: true, InstanceMutexName, out _isPrimaryInstance);

            // A second copy only signals the first one and exits (see OnStartup). It must not
            // register the icon-restore handler below, or exiting would un-hide the desktop
            // icons that the running instance deliberately hid.
            if (!_isPrimaryInstance || RestoreIconsOnly) return;

            // First start after the rename from OpenFences: bring the old settings along.
            if (!Pickets.Services.Sandbox.IsActive) Pickets.Services.LegacyMigration.Run();

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
            bool stuck = !_isPrimaryInstance && !RunningInstanceAnswers();
            if (!_isPrimaryInstance && !stuck)
                message = "Pickets is running. While it runs, your desktop items show inside fences instead of as " +
                          "desktop icons.\n\nTo get your normal desktop back, exit Pickets from its tray icon.";
            else
            {
                (message, icon) = ForceShowIconsMessage();
                if (stuck) { message = StuckMessage + "\n\n" + message; icon = System.Windows.MessageBoxImage.Warning; }
            }
            System.Windows.MessageBox.Show(message, "Pickets", System.Windows.MessageBoxButton.OK, icon);
            Shutdown();
        }

        private const string StuckMessage =
            "Another copy of Pickets is stuck and isn't responding. If Task Manager can't end it either, " +
            "restart Windows before starting Pickets again.";

        private static (string, System.Windows.MessageBoxImage) ForceShowIconsMessage() =>
            DesktopHelper.ForceShowDesktopIcons()
                ? ("Your desktop icons are back.", System.Windows.MessageBoxImage.Information)
                : ("Pickets couldn't bring your desktop icons back by itself.\n\n" +
                   "Right-click an empty spot on the desktop and choose View → Show desktop icons.",
                   System.Windows.MessageBoxImage.Warning);

        /// <summary>
        /// Whether the running Pickets answers within a few seconds. False when it's hung, or left
        /// behind after Windows couldn't end it: it still holds the single-instance lock, so a new
        /// launch would otherwise just hand over to it, and it will never show its window or put
        /// the desktop icons back. A Pickets from before 1.16.3 can't answer, so it counts as answering.
        /// </summary>
        private static bool RunningInstanceAnswers()
        {
            try
            {
                if (!EventWaitHandle.TryOpenExisting(PingEventName, out var ping)) return true;
                using (ping)
                {
                    if (!EventWaitHandle.TryOpenExisting(PongEventName, out var pong)) return true;
                    using (pong)
                    {
                        pong.Reset();
                        ping.Set();
                        return pong.WaitOne(TimeSpan.FromSeconds(5));
                    }
                }
            }
            catch { return true; /* can't tell: leave it be */ }
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
                // A stuck copy would never show its window: say so, and bring back the desktop
                // icons it may have left hidden.
                if (!RunningInstanceAnswers())
                {
                    var (message, _) = ForceShowIconsMessage();
                    System.Windows.MessageBox.Show(StuckMessage + "\n\n" + message, "Pickets",
                        System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Warning);
                    Shutdown();
                    return;
                }

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

            // Answered from the UI thread, so a hung Pickets doesn't (see RunningInstanceAnswers).
            _pingEvent = new EventWaitHandle(false, EventResetMode.AutoReset, PingEventName);
            _pongEvent = new EventWaitHandle(false, EventResetMode.AutoReset, PongEventName);
            var pong = _pongEvent;
            ThreadPool.RegisterWaitForSingleObject(_pingEvent, (_, __) =>
                Dispatcher.BeginInvoke(() => { try { pong.Set(); } catch { /* exiting */ } }),
                null, Timeout.Infinite, executeOnlyOnce: false);
        }

        protected override void OnExit(System.Windows.ExitEventArgs e)
        {
            _showEvent?.Dispose();
            _pingEvent?.Dispose();
            _pongEvent?.Dispose();
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
