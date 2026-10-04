using System.Windows;

namespace Pickets
{
    /// <summary>First-run welcome (also Help → Welcome tour). Offers to Auto-Import the desktop.</summary>
    public partial class WelcomeDialog : Window
    {
        public bool OrganizeRequested { get; private set; }

        public WelcomeDialog()
        {
            InitializeComponent();
            Pickets.Services.DarkTitleBar.Apply(this);
        }

        private void Organize_Click(object sender, RoutedEventArgs e)
        {
            OrganizeRequested = true;
            DialogResult = true;
        }

        private void Skip_Click(object sender, RoutedEventArgs e) => DialogResult = false;
    }
}
