using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows.Threading;

namespace OpenFences.Services
{
    /// <summary>
    /// Watches the user's Desktop folder and reports newly added items so the host can
    /// route them into fences via the rules engine. Events are debounced and marshalled
    /// onto the UI (STA) thread so the callback can safely use shell COM.
    /// </summary>
    public static class DesktopOrganizer
    {
        private static FileSystemWatcher? _watcher;
        private static Action<string>? _onNewItem;
        private static DispatcherTimer? _debounce;
        private static readonly HashSet<string> _pending = new(StringComparer.OrdinalIgnoreCase);

        public static bool IsRunning => _watcher != null;

        public static void Start(Action<string> onNewItem)
        {
            if (_watcher != null) return;
            _onNewItem = onNewItem;

            var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
            _watcher = new FileSystemWatcher(desktop)
            {
                IncludeSubdirectories = false,
                EnableRaisingEvents = true
            };
            _watcher.Created += (_, e) => Queue(e.FullPath);
            _watcher.Renamed += (_, e) => Queue(e.FullPath);
        }

        public static void Stop()
        {
            try { _watcher?.Dispose(); } catch { /* ignore */ }
            _watcher = null;
            _debounce?.Stop();
            _debounce = null;
            _pending.Clear();
        }

        private static void Queue(string path)
        {
            var disp = System.Windows.Application.Current?.Dispatcher;
            if (disp is null) return;

            // Coalesce the burst of events a single file copy produces.
            disp.BeginInvoke(new Action(() =>
            {
                _pending.Add(path);
                _debounce ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(700) };
                _debounce.Tick -= Flush;
                _debounce.Tick += Flush;
                _debounce.Stop();
                _debounce.Start();
            }));
        }

        private static void Flush(object? sender, EventArgs e)
        {
            _debounce?.Stop();
            var items = _pending.ToList();
            _pending.Clear();

            foreach (var p in items)
            {
                try
                {
                    if (File.Exists(p) || Directory.Exists(p))
                        _onNewItem?.Invoke(p);
                }
                catch { /* skip one bad item */ }
            }
        }
    }
}
