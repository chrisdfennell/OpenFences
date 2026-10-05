using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pickets
{
    /// <summary>How a tile shows its history: a line graph, a fill bar, a bar per core, or nothing.</summary>
    public enum MonitorVisual { None, Graph, Bar, Bars }

    /// <summary>What a system monitor tile can show (MonitorTileConfig.Kind holds these keys).</summary>
    public static class MonitorMetrics
    {
        public const string Cpu = "cpu";
        public const string Cores = "cores";
        public const string Memory = "memory";
        public const string Gpu = "gpu";
        public const string Disk = "disk";
        public const string DiskIo = "diskio";
        public const string Network = "network";
        public const string NetInfo = "netinfo";
        public const string Battery = "battery";
        public const string Clock = "clock";

        /// <summary>Every kind, in the order the Add tile menus list them.</summary>
        public static readonly string[] All = { Cpu, Cores, Memory, Gpu, Disk, DiskIo, Network, NetInfo, Battery, Clock };

        /// <summary>The readings a Pickets 1.13 monitor could show (FenceModel.Metrics).</summary>
        public static readonly string[] Original = { Cpu, Memory, Disk, Network, Battery };

        public static string Title(string kind) => kind switch
        {
            Cpu => "CPU",
            Cores => "CPU cores",
            Memory => "Memory",
            Gpu => "GPU",
            Disk => "Disk space",
            DiskIo => "Disk activity",
            Network => "Network",
            NetInfo => "Network info",
            Battery => "Battery",
            Clock => "Clock",
            _ => kind
        };

        public static string Description(string kind) => kind switch
        {
            Cpu => "Processor usage",
            Cores => "Usage of each processor core",
            Memory => "Memory in use",
            Gpu => "Graphics processor usage",
            Disk => "Free space on a drive",
            DiskIo => "Read and write speed of your drives",
            Network => "Download and upload speed",
            NetInfo => "Connection name and IP address",
            Battery => "Charge and time left",
            Clock => "Time and date",
            _ => ""
        };

        public static MonitorVisual Visual(string kind) => kind switch
        {
            Cpu or Memory or Gpu or DiskIo or Network => MonitorVisual.Graph,
            Disk or Battery => MonitorVisual.Bar,
            Cores => MonitorVisual.Bars,
            _ => MonitorVisual.None
        };
    }

    /// <summary>One tile in a system monitor fence: a big value, a detail line and a graph,
    /// a fill bar or a bar per core, built from a <see cref="MonitorTileConfig"/>.</summary>
    public class MonitorTile : INotifyPropertyChanged
    {
        public const int DefaultCapacity = 60;

        public MonitorTile(MonitorTileConfig config, string key, string label)
        {
            Config = config;
            Key = key;
            _label = label;
        }

        /// <summary>Convenience for tests and simple tiles.</summary>
        public MonitorTile(string kind, string key, string label)
            : this(new MonitorTileConfig { Kind = kind }, key, label) { }

        public MonitorTileConfig Config { get; }

        /// <summary>One of <see cref="MonitorMetrics"/>.</summary>
        public string Metric => Config.Kind;

        /// <summary>Tells tiles apart ("disk:C:", or the kind plus its place in the list).</summary>
        public string Key { get; }

        private string _label;
        public string Label { get => _label; set => Set(ref _label, value); }

        private string _value = "…";
        public string Value { get => _value; set => Set(ref _value, value); }

        private string _detail = "";
        public string Detail { get => _detail; set => Set(ref _detail, value); }

        /// <summary>What a screen reader says ("CPU, 37 percent").</summary>
        private string _spoken = "";
        public string Spoken { get => _spoken; set => Set(ref _spoken, value); }

        /// <summary>0 normal, 1 getting high (amber), 2 high (red); only with warning colors on.</summary>
        private int _level;
        public int Level { get => _level; set => Set(ref _level, value); }

        /// <summary>Disk and battery tiles show a fill bar (0…1).</summary>
        private double _fraction;
        public double Fraction { get => _fraction; set => Set(ref _fraction, Math.Clamp(value, 0, 1)); }

        private MonitorVisual Visual => Config.Graph ? MonitorMetrics.Visual(Metric) : MonitorVisual.None;
        public bool ShowsGraph => Visual == MonitorVisual.Graph;
        public bool ShowsBar => Visual == MonitorVisual.Bar;
        public bool ShowsBars => Visual == MonitorVisual.Bars;

        /// <summary>The graph was turned on or off in the tile's config.</summary>
        public void RefreshVisual()
        {
            OnChanged(nameof(ShowsGraph));
            OnChanged(nameof(ShowsBar));
            OnChanged(nameof(ShowsBars));
        }

        /// <summary>How many readings the graph spans (Settings → graph length ÷ interval).</summary>
        private int _capacity = DefaultCapacity;
        public int Capacity
        {
            get => _capacity;
            set
            {
                value = Math.Max(2, value);
                if (!Set(ref _capacity, value)) return;
                if (_history.Count > value) _history.RemoveRange(0, _history.Count - value);
                Graph = Scale(_history, _graphMax);
            }
        }

        /// <summary>Recent values, oldest first, scaled 0…1 for the graph.</summary>
        private IReadOnlyList<double> _graph = Array.Empty<double>();
        public IReadOnlyList<double> Graph { get => _graph; private set => Set(ref _graph, value); }

        /// <summary>CPU cores tile: each core's load, 0…1.</summary>
        private IReadOnlyList<double> _bars = Array.Empty<double>();
        public IReadOnlyList<double> Bars { get => _bars; set => Set(ref _bars, value); }

        private readonly List<double> _history = new();
        private double? _graphMax;

        /// <summary>Add a raw reading. <paramref name="max"/> fixes the graph's top (100 for a
        /// percentage); null scales it to the largest recent value (speeds).</summary>
        public void Push(double value, double? max)
        {
            _graphMax = max;
            _history.Add(Math.Max(0, value));
            if (_history.Count > Capacity) _history.RemoveRange(0, _history.Count - Capacity);
            Graph = Scale(_history, max);
        }

        /// <summary>Values scaled 0…1 against <paramref name="max"/>, or against the largest value
        /// (with a small floor so a quiet line doesn't fill the graph).</summary>
        public static IReadOnlyList<double> Scale(IReadOnlyList<double> values, double? max)
        {
            double top = max ?? Math.Max(MinAutoScale, Peak(values));
            var scaled = new double[values.Count];
            for (int i = 0; i < values.Count; i++)
                scaled[i] = top > 0 ? Math.Clamp(values[i] / top, 0, 1) : 0;
            return scaled;
        }

        // Speed graphs: below 64 KB/s everything sits near the bottom.
        private const double MinAutoScale = 64 * 1024;

        private static double Peak(IReadOnlyList<double> values)
        {
            double p = 0;
            foreach (var v in values) p = Math.Max(p, v);
            return p;
        }

        public override string ToString() => Spoken;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void OnChanged(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));

        private bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            OnChanged(name!);
            return true;
        }
    }
}
