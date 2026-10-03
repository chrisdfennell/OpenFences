using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;

using MessageBox = System.Windows.MessageBox;

namespace OpenFences
{
    /// <summary>Edits the auto-organize rules (Settings → Edit auto-organize rules…).</summary>
    public partial class RulesDialog : Window
    {
        public sealed class RuleRow : INotifyPropertyChanged
        {
            private RuleKind _kind;
            private string _extensionsText = "";
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
                    OnChanged(nameof(ExtensionsVisibility));
                }
            }

            public string KindLabel => LabelFor(Kind);
            public Visibility ExtensionsVisibility => Kind == RuleKind.Extensions ? Visibility.Visible : Visibility.Hidden;
            public string ExtensionsText { get => _extensionsText; set { _extensionsText = value; OnChanged(); } }
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
            _ => "Anything else"
        };

        private readonly ObservableCollection<RuleRow> _rows = new();
        private readonly IReadOnlyList<string> _fenceNames;

        public List<FenceRule> Rules { get; private set; } = new();
        public bool AutoOrganize => ChkAutoOrganize.IsChecked == true;
        public bool ApplyNow { get; private set; }

        public RulesDialog(IEnumerable<FenceRule> rules, bool autoOrganize, IEnumerable<string> fenceNames)
        {
            InitializeComponent();
            OpenFences.Services.DarkTitleBar.Apply(this);
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
                    ExtensionsText = string.Join(" ", r.Extensions),
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
            foreach (RuleKind k in new[] { RuleKind.Executable, RuleKind.Folder, RuleKind.Extensions, RuleKind.Any })
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
            var row = new RuleRow { Kind = RuleKind.Extensions, ExtensionsText = "", Target = "" };
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

                var exts = ParseExtensions(row.ExtensionsText);
                if (row.Kind == RuleKind.Extensions && exts.Count == 0)
                {
                    MessageBox.Show($"Rule {row.Number} needs at least one file type, like .png or .pdf.",
                                    "Auto-organize rules", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                rules.Add(new FenceRule
                {
                    Kind = row.Kind,
                    Extensions = row.Kind == RuleKind.Extensions ? exts : new List<string>(),
                    TargetFence = target
                });
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
