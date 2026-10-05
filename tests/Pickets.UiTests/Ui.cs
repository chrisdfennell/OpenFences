using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows.Threading;
using Xunit;

// Fence windows share static state (FenceWindow.AllFences, Options), so UI tests run one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Pickets.UiTests
{
    /// <summary>Runs a test body on its own STA thread with a WPF dispatcher, the way the app's
    /// UI thread works, and lets it pump messages while waiting for animations and timers.</summary>
    internal static class Ui
    {
        public static void Run(Action body)
        {
            Exception? error = null;
            var thread = new Thread(() =>
            {
                SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(Dispatcher.CurrentDispatcher));
                try { body(); }
                catch (Exception e) { error = e; }
                finally
                {
                    foreach (var f in new List<FenceWindow>(FenceWindow.AllFences)) f.Close();
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                    DeleteTempFolders();
                }
            });
            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            if (!thread.Join(TimeSpan.FromMinutes(2))) throw new TimeoutException("UI test didn't finish.");
            if (error != null) ExceptionDispatchInfo.Capture(error).Throw();
        }

        /// <summary>Process window messages, timers and animations for a while.</summary>
        public static void Pump(int ms)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            do
            {
                var frame = new DispatcherFrame();
                Dispatcher.CurrentDispatcher.BeginInvoke(DispatcherPriority.Background, () => frame.Continue = false);
                Dispatcher.PushFrame(frame);
                Thread.Sleep(10);
            } while (DateTime.UtcNow < until);
        }

        /// <summary>Pump until <paramref name="condition"/> holds, or give up after <paramref name="ms"/>.</summary>
        public static bool WaitUntil(Func<bool> condition, int ms = 5000)
        {
            var until = DateTime.UtcNow.AddMilliseconds(ms);
            while (!condition())
            {
                if (DateTime.UtcNow > until) return false;
                Pump(25);
            }
            return true;
        }

        private static readonly List<string> Temps = new();

        /// <summary>A fresh folder, deleted when the test is done.</summary>
        public static string TempFolder()
        {
            var dir = Path.Combine(Path.GetTempPath(), "Pickets.UiTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            lock (Temps) Temps.Add(dir);
            return dir;
        }

        public static void DeleteTempFolders()
        {
            lock (Temps)
            {
                foreach (var dir in Temps)
                    try { Directory.Delete(dir, recursive: true); } catch { /* still in use; the OS cleans temp */ }
                Temps.Clear();
            }
        }
    }
}
