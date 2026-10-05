using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace Pickets
{
    /// <summary>What a system monitor fence can show (FenceModel.Metrics holds these keys).</summary>
    public static class MonitorMetrics
    {
        public const string Cpu = "cpu";
        public const string Memory = "memory";
        public const string Disk = "disk";
        public const string Network = "network";
        public const string Battery = "battery";

        /// <summary>Every metric, in the order tiles appear.</summary>
        public static readonly string[] All = { Cpu, Memory, Disk, Network, Battery };

        public static string Title(string metric) => metric switch
        {
            Cpu => "CPU",
            Memory => "Memory",
            Disk => "Disk space",
            Network => "Network",
            Battery => "Battery",
            _ => metric
        };
    }

    /// <summary>One tile in a system monitor fence: a big value, a detail line and either a
    /// recent-history graph or a fill bar.</summary>
    public class MonitorTile : INotifyPropertyChanged
    {
        public const int HistoryLength = 60;

        public MonitorTile(string metric, string key, string label)
        {
            Metric = metric;
            Key = key;
            _label = label;
        }

        /// <summary>One of <see cref="MonitorMetrics"/>.</summary>
        public string Metric { get; }

        /// <summary>Tells tiles of the same metric apart (one disk tile per drive: "disk:C:").</summary>
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

        /// <summary>Disk and battery tiles show a fill bar (0…1) instead of a graph.</summary>
        private double _fraction;
        public double Fraction { get => _fraction; set => Set(ref _fraction, Math.Clamp(value, 0, 1)); }

        public bool ShowsBar => Metric is MonitorMetrics.Disk or MonitorMetrics.Battery;
        public bool ShowsGraph => !ShowsBar;

        /// <summary>Recent values, oldest first, scaled 0…1 for the graph.</summary>
        private IReadOnlyList<double> _graph = Array.Empty<double>();
        public IReadOnlyList<double> Graph { get => _graph; private set => Set(ref _graph, value); }

        private readonly List<double> _history = new();

        /// <summary>Add a raw reading. <paramref name="max"/> fixes the graph's top (100 for a
        /// percentage); null scales it to the largest recent value (network speed).</summary>
        public void Push(double value, double? max)
        {
            _history.Add(Math.Max(0, value));
            if (_history.Count > HistoryLength) _history.RemoveAt(0);
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

        // Network graphs: below 64 KB/s everything sits near the bottom.
        private const double MinAutoScale = 64 * 1024;

        private static double Peak(IReadOnlyList<double> values)
        {
            double p = 0;
            foreach (var v in values) p = Math.Max(p, v);
            return p;
        }

        public override string ToString() => Spoken;

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return;
            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }
    }
}
