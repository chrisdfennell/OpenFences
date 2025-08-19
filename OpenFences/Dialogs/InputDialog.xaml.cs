using System.Windows;

namespace OpenFences
{
    public partial class InputDialog : Window
    {
        public string Value => InputBox.Text;

        public InputDialog(string title, string prompt, string defaultValue = "")
        {
            InitializeComponent();
            TitleText.Text = title;
            PromptText.Text = prompt;
            InputBox.Text = defaultValue ?? "";
            Loaded += (_, __) => { InputBox.Focus(); InputBox.SelectAll(); };
        }

        private void OK_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }
    }
}
