using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

using MessageBox = Pickets.ThemedMessageBox;

namespace Pickets
{
    /// <summary>Edits the auto-organize rules (Settings → Edit auto-organize rules…).</summary>
    public partial class RulesDialog : Window
    {
        public sealed class RuleRow : INotifyPropertyChanged
        {
            private RuleKind _kind;
            private string _valueText = "";
            private string _target = "";
            private int _number;

            public RuleKind Kind
            {
                get => _kind;
                set
                {
                    _kind = value;
                    OnChanged();
                    OnChanged(nameof(KindLabel));
                    OnChanged(nameof(ValueVisibility));
                    OnChanged(nameof(ValueHint));
                }
            }

            public string KindLabel => LabelFor(Kind);
            public bool HasValue => Kind is RuleKind.Extensions or RuleKind.NamePattern or RuleKind.OlderThan or RuleKind.LargerThan;
            public Visibility ValueVisibility => HasValue ? Visibility.Visible : Visibility.Hidden;
            public string ValueHint => Kind switch
            {
                RuleKind.Extensions => "File types, e.g. .png .jpg .gif",
                RuleKind.NamePattern => "Names, e.g. invoice* screenshot* (* = anything)",
                RuleKind.OlderThan => "Days, e.g. 30 (tip: use “Save & sort Desktop fence now” to apply it to what's already there)",
                RuleKind.LargerThan => "Size in MB, e.g. 100",
                _ => ""
            };
            // File types, name patterns, or a number of days / MB, depending on Kind.
            public string ValueText { get => _valueText; set { _valueText = value; OnChanged(); } }
            public string Target { get => _target; set { _target = value; OnChanged(); } }
            public int Number { get => _number; set { _number = value; OnChanged(); } }

            public event PropertyChangedEventHandler? PropertyChanged;
            private void OnChanged([CallerMemberName] string? name = null) =>
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        internal static string LabelFor(RuleKind kind) => kind switch
        {
            RuleKind.Executable => "Apps and app shortcuts",
            RuleKind.Folder => "Folders",
            RuleKind.Extensions => "Files of type…",
            RuleKind.NamePattern => "Names like…",
            RuleKind.OlderThan => "Not changed in (days)…",
            RuleKind.LargerThan => "Larger than (MB)…",
            _ => "Anything else"
        };

        private static readonly RuleKind[] KindOrder =
        {
            RuleKind.Executable, RuleKind.Folder, RuleKind.Extensions, RuleKind.NamePattern,
            RuleKind.OlderThan, RuleKind.LargerThan, RuleKind.Any
        };

        private readonly ObservableCollection<RuleRow> _rows = new();
        private readonly IReadOnlyList<string> _fenceNames;

        public List<FenceRule> Rules { get; private set; } = new();
        public bool AutoOrganize => ChkAutoOrganize.IsChecked == true;
        public bool ApplyNow { get; private set; }

        public RulesDialog(IEnumerable<FenceRule> rules, bool autoOrganize, IEnumerable<string> fenceNames)
        {
            InitializeComponent();
            Pickets.Services.DarkTitleBar.Apply(this);
            _fenceNames = fenceNames.Distinct(StringComparer.OrdinalIgnoreCase).OrderBy(n => n).ToList();
            ChkAutoOrganize.IsChecked = autoOrganize;
            Load(rules);
            RuleList.ItemsSource = _rows;
            _rows.CollectionChanged += (_, __) => Renumber();
        }

        private void Load(IEnumerable<FenceRule> rules)
        {
            _rows.Clear();
            foreach (var r in rules)
                _rows.Add(new RuleRow
                {
                    Kind = r.Kind,
                    ValueText = r.Kind switch
                    {
                        RuleKind.Extensions => string.Join(" ", r.Extensions),
                        RuleKind.NamePattern => r.Pattern,
                        RuleKind.OlderThan or RuleKind.LargerThan =>
                            r.Amount.ToString(System.Globalization.CultureInfo.CurrentCulture),
                        _ => ""
                    },
                    Target = r.TargetFence
                });
            Renumber();
        }

        private void Renumber()
        {
            for (int i = 0; i < _rows.Count; i++) _rows[i].Number = i + 1;
        }

        private static RuleRow? RowOf(object sender) => (sender as FrameworkElement)?.DataContext as RuleRow;

        private void Kind_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || RowOf(sender) is not RuleRow row) return;
            var menu = new ContextMenu { PlacementTarget = fe, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            foreach (RuleKind k in KindOrder)
            {
                var mi = new MenuItem { Header = LabelFor(k), IsCheckable = true, IsChecked = row.Kind == k };
                mi.Click += (_, __) => row.Kind = k;
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }

        private void PickFence_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe || RowOf(sender) is not RuleRow row) return;
            var menu = new ContextMenu { PlacementTarget = fe, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            if (_fenceNames.Count == 0)
                menu.Items.Add(new MenuItem { Header = "(no fences yet — type a name)", IsEnabled = false });
            foreach (var name in _fenceNames)
            {
                var mi = new MenuItem { Header = name };
                mi.Click += (_, __) => row.Target = name;
                menu.Items.Add(mi);
            }
            menu.IsOpen = true;
        }

        private void Up_Click(object sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is not RuleRow row) return;
            int i = _rows.IndexOf(row);
            if (i > 0) _rows.Move(i, i - 1);
        }

        private void Down_Click(object sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is not RuleRow row) return;
            int i = _rows.IndexOf(row);
            if (i >= 0 && i < _rows.Count - 1) _rows.Move(i, i + 1);
        }

        private void Remove_Click(object sender, RoutedEventArgs e)
        {
            if (RowOf(sender) is RuleRow row) _rows.Remove(row);
        }

        private void Add_Click(object sender, RoutedEventArgs e)
        {
            // New rules go above a trailing "Anything else" catch-all, or they could never match.
            var row = new RuleRow { Kind = RuleKind.Extensions, ValueText = "", Target = "" };
            int at = _rows.Count;
            while (at > 0 && _rows[at - 1].Kind == RuleKind.Any) at--;
            _rows.Insert(at, row);
        }

        private void Reset_Click(object sender, RoutedEventArgs e) => Load(new AppConfig().Rules);

        private void Save_Click(object sender, RoutedEventArgs e) => TrySave(applyNow: false);

        private void SaveAndApply_Click(object sender, RoutedEventArgs e) => TrySave(applyNow: true);

        private void TrySave(bool applyNow)
        {
            var rules = new List<FenceRule>();
            foreach (var row in _rows)
            {
                var target = row.Target.Trim();
                if (target.Length == 0)
                {
                    MessageBox.Show($"Rule {row.Number} needs a fence to put items in.", "Auto-organize rules",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var rule = new FenceRule { Kind = row.Kind, TargetFence = target };
                string? problem = null;
                switch (row.Kind)
                {
                    case RuleKind.Extensions:
                        rule.Extensions = ParseExtensions(row.ValueText);
                        if (rule.Extensions.Count == 0) problem = "needs at least one file type, like .png or .pdf";
                        break;
                    case RuleKind.NamePattern:
                        rule.Pattern = row.ValueText.Trim();
                        if (Pickets.Services.PortalFilter.ParsePatterns(rule.Pattern).Count == 0)
                            problem = "needs at least one name, like invoice* or screenshot*";
                        break;
                    case RuleKind.OlderThan:
                    case RuleKind.LargerThan:
                        if (double.TryParse(row.ValueText.Trim(), System.Globalization.NumberStyles.Float,
                                            System.Globalization.CultureInfo.CurrentCulture, out var amount) && amount > 0)
                            rule.Amount = amount;
                        else
                            problem = row.Kind == RuleKind.OlderThan ? "needs a number of days, like 30"
                                                                     : "needs a size in MB, like 100";
                        break;
                }
                if (problem != null)
                {
                    MessageBox.Show($"Rule {row.Number} {problem}.", "Auto-organize rules",
                                    MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
                rules.Add(rule);
            }

            Rules = rules;
            ApplyNow = applyNow;
            DialogResult = true;
        }

        /// <summary>"png, .JPG;gif" → [".png", ".jpg", ".gif"]</summary>
        internal static List<string> ParseExtensions(string text) =>
            text.Split(new[] { ' ', ',', ';', '\t' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(t => t.Trim().TrimStart('*').ToLowerInvariant())
                .Select(t => t.StartsWith('.') ? t : "." + t)
                .Where(t => t.Length > 1)
                .Distinct()
                .ToList();
    }
}
