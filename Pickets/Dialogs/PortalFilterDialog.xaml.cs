using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Pickets.Services;

namespace Pickets
{
    /// <summary>Edits a folder portal's filter: file-name patterns and "changed within".</summary>
    public partial class PortalFilterDialog : Window
    {
        public string Patterns => PatternsBox.Text;
        public int? MaxAgeDays { get; private set; }

        public PortalFilterDialog(string? patterns, int? maxAgeDays)
        {
            InitializeComponent();
            DarkTitleBar.Apply(this);

            PatternsBox.Text = patterns ?? "";
            SetAge(maxAgeDays);
            Loaded += (_, __) => { PatternsBox.Focus(); PatternsBox.SelectAll(); };
        }

        private void SetAge(int? days)
        {
            MaxAgeDays = days is > 0 ? days : null;
            AgeText.Text = PortalFilter.AgeLabel(MaxAgeDays);
        }

        private void AgeButton_Click(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = AgeButton, Placement = PlacementMode.Bottom };
            foreach (var (days, label) in PortalFilter.AgeChoices)
            {
                var item = new MenuItem { Header = label, IsCheckable = true, IsChecked = days == MaxAgeDays };
                item.Click += (_, __) => SetAge(days);
                menu.Items.Add(item);
            }
            menu.IsOpen = true;
        }

        private void Clear_Click(object sender, RoutedEventArgs e)
        {
            PatternsBox.Text = "";
            SetAge(null);
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
        }
    }
}
