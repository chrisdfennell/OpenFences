using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Pickets.Services
{
    /// <summary>One reading of the PC's vitals (see <see cref="SystemMonitor"/>).</summary>
    public sealed class SystemSnapshot
    {
        public double CpuPercent { get; init; }
        public int Processors { get; init; }
        public ulong MemoryUsed { get; init; }
        public ulong MemoryTotal { get; init; }
        public IReadOnlyList<DriveSample> Drives { get; init; } = Array.Empty<DriveSample>();
        public double NetDownBytesPerSec { get; init; }
        public double NetUpBytesPerSec { get; init; }
        public BatterySample? Battery { get; init; }
    }

    public sealed record DriveSample(string Name, string Label, long TotalBytes, long FreeBytes);

    /// <param name="Percent">0…100.</param>
    /// <param name="SecondsLeft">Running on battery: the estimate, if Windows has one.</param>
    public sealed record BatterySample(double Percent, bool PluggedIn, bool Charging, int? SecondsLeft);

    /// <summary>
    /// Reads CPU, memory, disk space, network speed and battery for system monitor fences, every
    /// two seconds while at least one of them can be seen, from plain Windows APIs (nothing that
    /// needs admin rights). Readings are taken off the UI thread; <see cref="Sampled"/> is raised
    /// on it. Nothing is read while every monitor is rolled up, closed or hidden, or while the PC
    /// is locked.
    /// </summary>
    public static class SystemMonitor
    {
        public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2);

        /// <summary>A new reading (on the UI thread).</summary>
        public static event Action<SystemSnapshot>? Sampled;

        /// <summary>The latest reading, so a monitor that opens shows numbers straight away.</summary>
        public static SystemSnapshot? Latest { get; private set; }

        private static readonly HashSet<object> Viewers = new();
        private static DispatcherTimer? _timer;
        private static bool _locked, _busy, _sessionHooked;

        /// <summary>A monitor fence is (or stops being) visible. Sampling runs while any is.</summary>
        public static void SetViewing(object viewer, bool viewing)
        {
            if (viewing) Viewers.Add(viewer);
            else Viewers.Remove(viewer);
            UpdateTimer();
        }

        private static void UpdateTimer()
        {
            if (!_sessionHooked)
            {
                _sessionHooked = true;
                Microsoft.Win32.SystemEvents.SessionSwitch += (_, e) =>
                {
                    if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionLock) _locked = true;
                    else if (e.Reason == Microsoft.Win32.SessionSwitchReason.SessionUnlock) _locked = false;
                    System.Windows.Application.Current?.Dispatcher.BeginInvoke(UpdateTimer);
                };
            }

            bool run = Viewers.Count > 0 && !_locked;
            if (run && _timer == null)
            {
                _timer = new DispatcherTimer { Interval = Interval };
                _timer.Tick += (_, __) => SampleNow();
            }
            if (_timer == null) return;
            if (run && !_timer.IsEnabled) { _timer.Start(); SampleNow(); }
            else if (!run && _timer.IsEnabled) _timer.Stop();
        }

        private static async void SampleNow()
        {
            if (_busy) return; // a slow reading (network adapters) is still running
            _busy = true;
            try
            {
                var snap = await Task.Run(Read);
                Latest = snap;
                Sampled?.Invoke(snap);
            }
            catch (Exception ex)
            {
                (System.Windows.Application.Current as App)?.SafeLog("System monitor", ex);
            }
            finally { _busy = false; }
        }

        // ---------- Readings ----------
        private static CpuTimes? _lastCpu;
        private static Dictionary<string, (long down, long up)>? _lastNet;
        private static DateTime _lastNetAt;

        private static SystemSnapshot Read()
        {
            var (used, total) = ReadMemory();
            var (down, up) = ReadNetwork();
            return new SystemSnapshot
            {
                CpuPercent = ReadCpu(),
                Processors = Environment.ProcessorCount,
                MemoryUsed = used,
                MemoryTotal = total,
                Drives = ReadDrives(),
                NetDownBytesPerSec = down,
                NetUpBytesPerSec = up,
                Battery = ReadBattery(),
            };
        }

        private static double ReadCpu()
        {
            if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
            var now = new CpuTimes(idle, kernel, user);
            var prev = _lastCpu;
            _lastCpu = now;
            return prev is { } p ? CpuPercent(p, now) : 0;
        }

        private static (ulong used, ulong total) ReadMemory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
            return (m.ullTotalPhys - m.ullAvailPhys, m.ullTotalPhys);
        }

        // Fixed drives only: network and removable drives can stall for seconds when unreachable.
        private static IReadOnlyList<DriveSample> ReadDrives()
        {
            var list = new List<DriveSample>();
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (d.DriveType != DriveType.Fixed || !d.IsReady || d.TotalSize <= 0) continue;
                    list.Add(new DriveSample(d.Name.TrimEnd('\\'), d.VolumeLabel, d.TotalSize, d.TotalFreeSpace));
                }
                catch { /* skip this drive */ }
            }
            return list;
        }

        private static (double down, double up) ReadNetwork()
        {
            var now = DateTime.UtcNow;
            var current = new Dictionary<string, (long down, long up)>();
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var s = nic.GetIPStatistics();
                    current[nic.Id] = (s.BytesReceived, s.BytesSent);
                }
                catch { /* adapter went away */ }
            }

            var prev = _lastNet;
            double seconds = (now - _lastNetAt).TotalSeconds;
            _lastNet = current;
            _lastNetAt = now;
            if (prev == null) return (0, 0);
            return NetworkRates(prev, current, seconds);
        }

        private static BatterySample? ReadBattery()
        {
            var p = WinForms.SystemInformation.PowerStatus;
            if (p.BatteryChargeStatus.HasFlag(WinForms.BatteryChargeStatus.NoSystemBattery) ||
                p.BatteryChargeStatus == WinForms.BatteryChargeStatus.Unknown ||
                p.BatteryLifePercent is < 0 or > 1) return null;
            bool plugged = p.PowerLineStatus == WinForms.PowerLineStatus.Online;
            return new BatterySample(
                Math.Round(p.BatteryLifePercent * 100),
                plugged,
                p.BatteryChargeStatus.HasFlag(WinForms.BatteryChargeStatus.Charging),
                !plugged && p.BatteryLifeRemaining > 0 ? p.BatteryLifeRemaining : null);
        }

        // ---------- Arithmetic (kept apart from the Windows calls so it can be tested) ----------

        /// <summary>Cumulative idle/kernel/user time (100 ns units). Kernel time includes idle time.</summary>
        public readonly record struct CpuTimes(long Idle, long Kernel, long User);

        /// <summary>The share of time the processors were busy between two readings, 0…100.</summary>
        public static double CpuPercent(CpuTimes before, CpuTimes after)
        {
            long idle = after.Idle - before.Idle;
            long total = (after.Kernel - before.Kernel) + (after.User - before.User);
            if (total <= 0 || idle < 0) return 0;
            return Math.Clamp(100.0 * (total - idle) / total, 0, 100);
        }

        /// <summary>Bytes per second down and up, from per-adapter byte counters. Adapters that
        /// appear, disappear or reset their counters between readings are left out.</summary>
        public static (double down, double up) NetworkRates(
            IReadOnlyDictionary<string, (long down, long up)> before,
            IReadOnlyDictionary<string, (long down, long up)> after,
            double seconds)
        {
            if (seconds <= 0) return (0, 0);
            long down = 0, up = 0;
            foreach (var (id, a) in after)
            {
                if (!before.TryGetValue(id, out var b)) continue;
                if (a.down >= b.down) down += a.down - b.down;
                if (a.up >= b.up) up += a.up - b.up;
            }
            return (down / seconds, up / seconds);
        }

        /// <summary>"512 B", "3.4 KB", "12 MB", "1.5 TB" (1024-based, like Explorer).</summary>
        public static string FormatBytes(double bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            int u = 0;
            while (bytes >= 1024 && u < units.Length - 1) { bytes /= 1024; u++; }
            string number = u == 0 || bytes >= 100 ? Math.Round(bytes).ToString("0")
                          : bytes >= 10 ? bytes.ToString("0") : bytes.ToString("0.#");
            return number + " " + units[u];
        }

        /// <summary>"1.2 MB/s".</summary>
        public static string FormatRate(double bytesPerSec) => FormatBytes(Math.Max(0, bytesPerSec)) + "/s";

        /// <summary>"3 h 12 min", "45 min", "less than a minute".</summary>
        public static string FormatDuration(int seconds)
        {
            if (seconds < 60) return "less than a minute";
            int h = seconds / 3600, m = seconds % 3600 / 60;
            return h == 0 ? $"{m} min" : m == 0 ? $"{h} h" : $"{h} h {m} min";
        }

        // ---------- Win32 ----------
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        [StructLayout(LayoutKind.Sequential)]
        private struct MEMORYSTATUSEX
        {
            public uint dwLength;
            public uint dwMemoryLoad;
            public ulong ullTotalPhys;
            public ulong ullAvailPhys;
            public ulong ullTotalPageFile;
            public ulong ullAvailPageFile;
            public ulong ullTotalVirtual;
            public ulong ullAvailVirtual;
            public ulong ullAvailExtendedVirtual;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX buffer);
    }
}
