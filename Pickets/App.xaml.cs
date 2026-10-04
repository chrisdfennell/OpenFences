using System;
using System.IO;
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
            if (!_isPrimaryInstance) return;

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

        protected override void OnStartup(System.Windows.StartupEventArgs e)
        {
            base.OnStartup(e);

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
