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
        /// <summary>The address the internet sees, when Show public IP is on and it's known.</summary>
        public string? PublicAddress { get; init; }
        /// <summary>Show public IP is on but the lookup didn't work (offline, blocked…).</summary>
        public bool PublicAddressFailed { get; init; }
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
    /// <see cref="Sampled"/> is raised on it. A rolled-up monitor reads only what its graphs
    /// need; nothing is read while every monitor is closed or hidden, while a fullscreen game or
    /// app is in front, or while the PC is locked.
    /// </summary>
    public static class SystemMonitor
    {
        /// <summary>A new reading (on the UI thread).</summary>
        public static event Action<SystemSnapshot>? Sampled;

        /// <summary>A reading was skipped because a fullscreen game or app was in front (on the UI thread).</summary>
        public static event Action? Skipped;

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

            bool run = Viewers.Count > 0 && !_locked && !_fixed;
            if (run && _timer == null)
            {
                _timer = new DispatcherTimer { Interval = Interval };
                _timer.Tick += (_, __) => Tick();
            }
            if (_timer == null) return;
            if (run && !_timer.IsEnabled) { _timer.Start(); Tick(); }
            else if (!run && _timer.IsEnabled) _timer.Stop();
        }

        // Nothing is read while a fullscreen game or app is in front, so monitors cost a game
        // nothing; graphs leave a gap for that time.
        private static void Tick()
        {
            if (FullscreenAppInFront()) { Skipped?.Invoke(); return; }
            SampleNow();
        }

        private static bool FullscreenAppInFront()
        {
            // 2: a fullscreen app (borderless games, videos), 3: a Direct3D fullscreen game,
            // 4: presentation mode. A cheap call, unlike the readings it saves.
            try { return SHQueryUserNotificationState(out int state) == 0 && state is 2 or 3 or 4; }
            catch { return false; }
        }

        private static async void SampleNow()
        {
            if (_busy) return; // a slow reading (network adapters) is still running
            _busy = true;
            try
            {
                var needs = _needs;
                var snap = await Task.Run(() => Read(needs));
                if (_fixed) return; // switched to made-up readings meanwhile
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
            if (_fixed) return true;
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

        // ---------- Made-up readings (tools/Screenshots) ----------
        private static bool _fixed;

        /// <summary>Stop reading this PC and show <paramref name="readings"/> instead, oldest first so
        /// the graphs have some history. Every kind of tile counts as available. For screenshots,
        /// which mustn't show anyone's own PC (its address, drives…).</summary>
        internal static void ShowFixed(IEnumerable<SystemSnapshot> readings)
        {
            _fixed = true;
            _timer?.Stop();
            foreach (var r in readings)
            {
                Latest = r;
                Sampled?.Invoke(r);
            }
        }

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
            var pub = Needs(MonitorMetrics.NetInfo) ? PublicIp(net.address) : (null, false);
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
                PublicAddress = pub.address,
                PublicAddressFailed = pub.failed,
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

        // ---------- Public IP (only when the user turns it on) ----------
        // The one reading that isn't on this PC: the address the internet sees comes from asking
        // a service out there, which sees the address too. So it's off unless the user turns on
        // Show public IP (privacy policy), asked again only when the connection changes or every
        // 15 minutes, never while offline, and never without a Network info tile showing.

        public const string PublicIpService = "https://ipv4.icanhazip.com/";
        public static readonly TimeSpan PublicIpRefresh = TimeSpan.FromMinutes(15);

        private static readonly object PublicIpLock = new();
        private static bool _publicIpEnabled, _publicIpBusy, _publicIpFailed;
        private static string? _publicIp, _publicIpFor;
        private static DateTime _publicIpAt;
        private static System.Net.Http.HttpClient? _publicIpHttp;

        /// <summary>Settings → Show public IP.</summary>
        public static bool PublicIpEnabled
        {
            get => _publicIpEnabled;
            set
            {
                lock (PublicIpLock)
                {
                    _publicIpEnabled = value;
                    // Forget it when turned off, so turning it on again asks straight away.
                    if (!value) { _publicIp = _publicIpFor = null; _publicIpFailed = false; _publicIpAt = default; }
                }
            }
        }

        /// <summary>The last public address (or that the lookup failed), starting a new lookup
        /// in the background when one is due.</summary>
        private static (string? address, bool failed) PublicIp(string? localAddress)
        {
            lock (PublicIpLock)
            {
                if (!_publicIpEnabled) return (null, false);
                if (localAddress != null && !_publicIpBusy &&
                    PublicIpDue(DateTime.UtcNow, _publicIpAt, _publicIpFor, localAddress))
                {
                    _publicIpBusy = true;
                    _ = LookUpPublicIp(localAddress);
                }
                return PublicIpToShow(localAddress, _publicIpFor, _publicIp, _publicIpFailed);
            }
        }

        /// <summary>What to show for <paramref name="localAddress"/>: an answer only counts for the
        /// connection it was asked on, so after switching networks the tile says "looking up…"
        /// (null, not failed) instead of the old network's public address.</summary>
        internal static (string? address, bool failed) PublicIpToShow(
            string? localAddress, string? answeredFor, string? address, bool failed) =>
            localAddress == null || answeredFor != localAddress
                ? (null, false)
                : (address, failed && address == null);

        private static async Task LookUpPublicIp(string localAddress)
        {
            string? found = null;
            try
            {
                _publicIpHttp ??= CreatePublicIpClient();
                found = ParsePublicIp(await _publicIpHttp.GetStringAsync(PublicIpService).ConfigureAwait(false));
            }
            catch { /* offline, blocked, timed out: shown as unavailable until the next try */ }
            lock (PublicIpLock)
            {
                _publicIpBusy = false;
                if (!_publicIpEnabled) return; // turned off meanwhile
                // A new connection with no answer yet shouldn't keep showing the old network's address.
                if (found != null || _publicIpFor != localAddress) _publicIp = found;
                _publicIpFailed = found == null;
                _publicIpFor = localAddress;
                _publicIpAt = DateTime.UtcNow;
            }
        }

        private static System.Net.Http.HttpClient CreatePublicIpClient()
        {
            var http = new System.Net.Http.HttpClient { Timeout = TimeSpan.FromSeconds(10) };
            http.DefaultRequestHeaders.UserAgent.ParseAdd("Pickets/" + UpdateService.CurrentVersion.ToString(3));
            return http;
        }

        /// <summary>Ask again when the connection changed (a different local address) or the last
        /// answer is old.</summary>
        internal static bool PublicIpDue(DateTime now, DateTime lastAt, string? lastFor, string localAddress) =>
            lastFor != localAddress || now - lastAt >= PublicIpRefresh;

        /// <summary>The service's answer ("203.0.113.7\n"), or null if it isn't an IP address.</summary>
        internal static string? ParsePublicIp(string? body)
        {
            var text = body?.Trim() ?? "";
            // IPAddress also takes shorthand like "123" (0.0.0.123); a real answer is dotted or IPv6.
            if (!text.Contains('.') && !text.Contains(':')) return null;
            return System.Net.IPAddress.TryParse(text, out var ip) &&
                   ip.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
                ? ip.ToString() : null;
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

        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int state);
    }
}
