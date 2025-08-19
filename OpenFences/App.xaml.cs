using System;
using System.IO;
using System.Threading.Tasks;

namespace OpenFences
{
    // Fully-qualify to avoid WinForms ambiguity if present
    public partial class App : System.Windows.Application
    {
        private readonly string _logPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "OpenFences", "error.log");

        public App()
        {
            this.DispatcherUnhandledException += (s, e) =>
            {
                SafeLog("DispatcherUnhandledException", e.Exception);
                System.Windows.MessageBox.Show("Unexpected error (UI thread). Details were logged.\n" + e.Exception.Message,
                    "OpenFences", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Error);
                e.Handled = true; // keep app alive
            };

            AppDomain.CurrentDomain.UnhandledException += (s, e) =>
            {
                SafeLog("AppDomain.UnhandledException", e.ExceptionObject as Exception);
            };

            TaskScheduler.UnobservedTaskException += (s, e) =>
            {
                SafeLog("TaskScheduler.UnobservedTaskException", e.Exception);
                e.SetObserved();
            };
        }

        private void SafeLog(string tag, Exception? ex)
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
