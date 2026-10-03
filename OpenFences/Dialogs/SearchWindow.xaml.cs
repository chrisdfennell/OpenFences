using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;

using WinForms = System.Windows.Forms;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace OpenFences
{
    /// <summary>
    /// Quick search across every fence (Ctrl+Alt+F). While it's open, fences are lifted above
    /// app windows, non-matching tiles fade out and rolled-up fences with matches open.
    /// </summary>
    public partial class SearchWindow : Window
    {
        public sealed record Result(FenceItem Item, string FenceName);

        private const int MaxResults = 50;
        private readonly List<FenceWindow> _fences;
        private bool _closing;

        public SearchWindow(IEnumerable<FenceWindow> fences)
        {
            InitializeComponent();
            _fences = fences.ToList();

            // Raise first, so this window (activated afterwards) stays on top of them.
            foreach (var f in _fences.Where(f => f.IsVisible)) f.SetRaised(true);

            PlaceOnCursorMonitor();
            PreviewKeyDown += SearchWindow_PreviewKeyDown;
            Deactivated += (_, __) => SafeClose();
            Closed += (_, __) =>
            {
                foreach (var f in _fences)
                {
                    f.ApplySearch(null);
                    if (f.IsVisible) f.SetRaised(false);
                }
            };
            Loaded += (_, __) => { Activate(); QueryBox.Focus(); };
            Refresh();
        }

        // Upper third of whichever monitor the cursor is on.
        private void PlaceOnCursorMonitor()
        {
            var area = WinForms.Screen.FromPoint(WinForms.Cursor.Position).WorkingArea;
            double s = OpenFences.Services.ScreenLayout.Scale;
            Left = area.Left / s + (area.Width / s - Width) / 2;
            Top = area.Top / s + area.Height / s * 0.2;
        }

        private void QueryBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e) => Refresh();

        private void Refresh()
        {
            string q = QueryBox.Text.Trim();
            Placeholder.Visibility = QueryBox.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

            var results = new List<Result>();
            foreach (var f in _fences)
            {
                // Hidden fences still show up in the list; only visible ones get highlighted.
                if (f.IsVisible) f.ApplySearch(q.Length == 0 ? null : q);
                if (q.Length == 0) continue;
                foreach (var item in f.ItemsSource)
                    if (item.DisplayName.Contains(q, StringComparison.OrdinalIgnoreCase))
                        results.Add(new Result(item, f.FenceName));
            }

            // Names that start with the query first, then alphabetical.
            var ordered = results
                .OrderBy(r => r.Item.DisplayName.StartsWith(q, StringComparison.OrdinalIgnoreCase) ? 0 : 1)
                .ThenBy(r => r.Item.DisplayName, StringComparer.OrdinalIgnoreCase)
                .Take(MaxResults)
                .ToList();

            Results.ItemsSource = ordered;
            Results.Visibility = ordered.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            if (ordered.Count > 0) Results.SelectedIndex = 0;

            Hint.Text = q.Length > 0 && ordered.Count == 0
                ? "No matches"
                : "↑↓ choose · Enter open · Esc close";
        }

        private void SearchWindow_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            int count = Results.Items.Count;
            switch (e.Key)
            {
                case Key.Escape:
                    SafeClose();
                    e.Handled = true;
                    break;
                case Key.Down when count > 0:
                    Results.SelectedIndex = (Results.SelectedIndex + 1) % count;
                    Results.ScrollIntoView(Results.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Up when count > 0:
                    Results.SelectedIndex = (Results.SelectedIndex - 1 + count) % count;
                    Results.ScrollIntoView(Results.SelectedItem);
                    e.Handled = true;
                    break;
                case Key.Enter:
                    OpenSelected();
                    e.Handled = true;
                    break;
            }
        }

        private void Results_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) => OpenSelected();

        private void OpenSelected()
        {
            if (Results.SelectedItem is not Result r) return;
            SafeClose();
            FenceWindow.LaunchPath(r.Item.Path);
        }

        // Deactivated fires while Close() is already running; don't close twice.
        private void SafeClose()
        {
            if (_closing) return;
            _closing = true;
            Close();
        }
    }
}
