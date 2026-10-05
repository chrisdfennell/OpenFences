using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Threading;
using WinForms = System.Windows.Forms;

namespace Pickets.Services
{
    /// <summary>One reading of the PC's vitals (see <see cref="SystemMonitor"/>). Readings no
    /// visible tile asked for are left at their defaults.</summary>
    public sealed class SystemSnapshot
    {
        public double CpuPercent { get; init; }
        public int Processors { get; init; }
        /// <summary>Each core's load, 0…100, in core order (empty if not read).</summary>
        public IReadOnlyList<double> CoreLoads { get; init; } = Array.Empty<double>();
        public ulong MemoryUsed { get; init; }
        public ulong MemoryTotal { get; init; }
        /// <summary>Null when this PC has no GPU counters (or they weren't read).</summary>
        public double? GpuPercent { get; init; }
        public IReadOnlyList<DriveSample> Drives { get; init; } = Array.Empty<DriveSample>();
        /// <summary>Null when the disk counters aren't available (or weren't read).</summary>
        public double? DiskReadBytesPerSec { get; init; }
        public double? DiskWriteBytesPerSec { get; init; }
        public double NetDownBytesPerSec { get; init; }
        public double NetUpBytesPerSec { get; init; }
        /// <summary>The connection that reaches the internet: its name ("Wi-Fi") and IPv4 address.</summary>
        public string? NetName { get; init; }
        public string? NetAddress { get; init; }
        public BatterySample? Battery { get; init; }
    }

    public sealed record DriveSample(string Name, string Label, long TotalBytes, long FreeBytes);

    /// <param name="Percent">0…100.</param>
    /// <param name="SecondsLeft">Running on battery: the estimate, if Windows has one.</param>
    public sealed record BatterySample(double Percent, bool PluggedIn, bool Charging, int? SecondsLeft);

    /// <summary>
    /// Reads the PC's vitals for system monitor fences, from plain Windows APIs and performance
    /// counters (nothing that needs admin rights), every few seconds while at least one monitor
    /// can be seen, and only the readings its tiles show. Readings are taken off the UI thread;
    /// <see cref="Sampled"/> is raised on it. Nothing is read while every monitor is rolled up,
    /// closed or hidden, or while the PC is locked.
    /// </summary>
    public static class SystemMonitor
    {
        /// <summary>A new reading (on the UI thread).</summary>
        public static event Action<SystemSnapshot>? Sampled;

        /// <summary>The latest reading, so a monitor that opens shows numbers straight away.</summary>
        public static SystemSnapshot? Latest { get; private set; }

        public static TimeSpan Interval { get; private set; } = TimeSpan.FromSeconds(2);

        private static readonly Dictionary<object, HashSet<string>> Viewers = new();
        private static HashSet<string> _needs = new();
        private static DispatcherTimer? _timer;
        private static bool _locked, _busy, _sessionHooked;

        /// <summary>Settings → how often monitors read.</summary>
        public static void SetInterval(int seconds)
        {
            Interval = TimeSpan.FromSeconds(Math.Clamp(seconds, 1, 60));
            if (_timer != null) _timer.Interval = Interval;
        }

        /// <summary>A monitor fence can be seen and shows these kinds of tile (or, with null,
        /// can't be seen). Sampling runs while any can, for the kinds they show.</summary>
        public static void SetViewing(object viewer, IEnumerable<string>? kinds)
        {
            bool had = Viewers.ContainsKey(viewer);
            if (kinds == null) Viewers.Remove(viewer);
            else Viewers[viewer] = new HashSet<string>(kinds);
            var needs = new HashSet<string>(Viewers.Values.SelectMany(k => k));
            bool more = !needs.IsSubsetOf(_needs);
            _needs = needs;
            UpdateTimer();
            // A newly added kind of tile gets its first numbers now, not at the next tick.
            if (kinds != null && had && more && _timer?.IsEnabled == true) SampleNow();
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
                var needs = _needs;
                var snap = await Task.Run(() => Read(needs));
                Latest = snap;
                Sampled?.Invoke(snap);
            }
            catch (Exception ex)
            {
                (System.Windows.Application.Current as App)?.SafeLog("System monitor", ex);
            }
            finally { _busy = false; }
        }

        // ---------- What this PC has ----------
        private static readonly Dictionary<string, bool> Available = new();

        /// <summary>False for a reading this PC can't give: no battery, or no GPU or disk
        /// counters. Settings marks those tiles, and monitors leave them out.</summary>
        public static bool IsAvailable(string kind)
        {
            if (kind == MonitorMetrics.Battery) return ReadBattery() != null;
            if (kind is not (MonitorMetrics.Gpu or MonitorMetrics.Cores or MonitorMetrics.DiskIo)) return true;
            lock (Available)
            {
                if (Available.TryGetValue(kind, out var known)) return known;
                using var probe = PdhCounter.TryOpen(kind switch
                {
                    MonitorMetrics.Gpu => GpuPath,
                    MonitorMetrics.Cores => CoresPath,
                    _ => DiskReadPath
                });
                // A PC without a GPU driver that reports usage has the counter but no instances.
                bool ok = probe != null && (kind != MonitorMetrics.Gpu || (probe.Collect() && probe.Values().Count > 0));
                return Available[kind] = ok;
            }
        }

        /// <summary>The fixed drives ("C:"), for disk space tiles.</summary>
        public static List<DriveSample> FixedDrives() => ReadDrives();

        // ---------- Readings ----------
        private const string GpuPath = @"\GPU Engine(*)\Utilization Percentage";
        private const string CoresPath = @"\Processor(*)\% Processor Time";
        private const string DiskReadPath = @"\PhysicalDisk(_Total)\Disk Read Bytes/sec";
        private const string DiskWritePath = @"\PhysicalDisk(_Total)\Disk Write Bytes/sec";

        private static CpuTimes? _lastCpu;
        private static Dictionary<string, (long down, long up)>? _lastNet;
        private static DateTime _lastNetAt;
        private static PdhCounter? _gpu, _cores, _diskRead, _diskWrite;
        private static bool _gpuTried, _coresTried, _diskTried;

        private static SystemSnapshot Read(HashSet<string> needs)
        {
            bool Needs(params string[] kinds) => kinds.Any(needs.Contains);

            var (used, total) = Needs(MonitorMetrics.Memory) ? ReadMemory() : (0UL, 0UL);
            var net = Needs(MonitorMetrics.Network, MonitorMetrics.NetInfo) ? ReadNetwork() : default;
            var (read, write) = Needs(MonitorMetrics.DiskIo) ? ReadDiskIo() : (null, null);
            return new SystemSnapshot
            {
                CpuPercent = Needs(MonitorMetrics.Cpu) ? ReadCpu() : 0,
                Processors = Environment.ProcessorCount,
                CoreLoads = Needs(MonitorMetrics.Cores) ? ReadCores() : Array.Empty<double>(),
                MemoryUsed = used,
                MemoryTotal = total,
                GpuPercent = Needs(MonitorMetrics.Gpu) ? ReadGpu() : null,
                Drives = Needs(MonitorMetrics.Disk) ? ReadDrives() : Array.Empty<DriveSample>(),
                DiskReadBytesPerSec = read,
                DiskWriteBytesPerSec = write,
                NetDownBytesPerSec = net.down,
                NetUpBytesPerSec = net.up,
                NetName = net.name,
                NetAddress = net.address,
                Battery = Needs(MonitorMetrics.Battery) ? ReadBattery() : null,
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

        private static IReadOnlyList<double> ReadCores()
        {
            if (!_coresTried) { _coresTried = true; _cores = PdhCounter.TryOpen(CoresPath); }
            if (_cores == null || !_cores.Collect()) return Array.Empty<double>();
            return CoreLoads(_cores.Values());
        }

        private static double? ReadGpu()
        {
            if (!_gpuTried) { _gpuTried = true; _gpu = PdhCounter.TryOpen(GpuPath); }
            if (_gpu == null || !_gpu.Collect()) return null;
            return GpuPercent(_gpu.Values());
        }

        private static (double?, double?) ReadDiskIo()
        {
            if (!_diskTried)
            {
                _diskTried = true;
                _diskRead = PdhCounter.TryOpen(DiskReadPath);
                _diskWrite = PdhCounter.TryOpen(DiskWritePath);
            }
            double? r = _diskRead != null && _diskRead.Collect() ? _diskRead.Value() : null;
            double? w = _diskWrite != null && _diskWrite.Collect() ? _diskWrite.Value() : null;
            return (r, w);
        }

        private static (ulong used, ulong total) ReadMemory()
        {
            var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (!GlobalMemoryStatusEx(ref m)) return (0, 0);
            return (m.ullTotalPhys - m.ullAvailPhys, m.ullTotalPhys);
        }

        // Fixed drives only: network and removable drives can stall for seconds when unreachable.
        private static List<DriveSample> ReadDrives()
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

        private static (double down, double up, string? name, string? address) ReadNetwork()
        {
            var now = DateTime.UtcNow;
            var current = new Dictionary<string, (long down, long up)>();
            string? name = null, address = null;
            foreach (var nic in NetworkInterface.GetAllNetworkInterfaces())
            {
                try
                {
                    if (nic.OperationalStatus != OperationalStatus.Up ||
                        nic.NetworkInterfaceType is NetworkInterfaceType.Loopback or NetworkInterfaceType.Tunnel) continue;
                    var s = nic.GetIPStatistics();
                    current[nic.Id] = (s.BytesReceived, s.BytesSent);

                    // The connection with a gateway is the one that reaches the internet.
                    if (address == null)
                    {
                        var props = nic.GetIPProperties();
                        if (props.GatewayAddresses.Any(g => !g.Address.Equals(System.Net.IPAddress.Any)))
                        {
                            var v4 = props.UnicastAddresses.FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork);
                            if (v4 != null) { name = nic.Name; address = v4.Address.ToString(); }
                        }
                    }
                }
                catch { /* adapter went away */ }
            }

            var prev = _lastNet;
            double seconds = (now - _lastNetAt).TotalSeconds;
            _lastNet = current;
            _lastNetAt = now;
            var (down, up) = prev == null ? (0, 0) : NetworkRates(prev, current, seconds);
            return (down, up, name, address);
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

        /// <summary>Each core's load (0…100) in core order, from the "\Processor(*)" instances
        /// ("0", "1", … and "_Total", which is left out).</summary>
        public static IReadOnlyList<double> CoreLoads(IEnumerable<(string Instance, double Value)> values) =>
            values.Where(v => int.TryParse(v.Instance, NumberStyles.Integer, CultureInfo.InvariantCulture, out _))
                  .OrderBy(v => int.Parse(v.Instance, CultureInfo.InvariantCulture))
                  .Select(v => Math.Clamp(v.Value, 0, 100))
                  .ToList();

        /// <summary>GPU usage the way Task Manager shows it: each engine's use added up over every
        /// app ("pid_…_luid_…_phys_0_eng_0_engtype_3D"), and the busiest engine wins.</summary>
        public static double GpuPercent(IEnumerable<(string Instance, double Value)> values)
        {
            var perEngine = new Dictionary<string, double>(StringComparer.OrdinalIgnoreCase);
            foreach (var (instance, value) in values)
            {
                int at = instance.IndexOf("luid_", StringComparison.OrdinalIgnoreCase);
                var engine = at >= 0 ? instance[at..] : instance;
                perEngine[engine] = perEngine.GetValueOrDefault(engine) + Math.Max(0, value);
            }
            return perEngine.Count == 0 ? 0 : Math.Clamp(perEngine.Values.Max(), 0, 100);
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
        public static string FormatBytes(double bytes) => Scaled(bytes, 1024, new[] { "B", "KB", "MB", "GB", "TB", "PB" });

        /// <summary>"1.2 MB/s", or in bits "9.6 Mbps" (1000-based, like internet speeds).</summary>
        public static string FormatRate(double bytesPerSec, bool bits = false) =>
            bits ? Scaled(Math.Max(0, bytesPerSec) * 8, 1000, new[] { "bps", "Kbps", "Mbps", "Gbps", "Tbps" })
                 : FormatBytes(Math.Max(0, bytesPerSec)) + "/s";

        private static string Scaled(double value, double step, string[] units)
        {
            int u = 0;
            while (value >= step && u < units.Length - 1) { value /= step; u++; }
            string number = u == 0 || value >= 100 ? Math.Round(value).ToString("0")
                          : value >= 10 ? value.ToString("0") : value.ToString("0.#");
            return number + " " + units[u];
        }

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
