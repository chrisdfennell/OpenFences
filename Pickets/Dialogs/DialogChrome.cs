using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;

namespace Pickets
{
    /// <summary>
    /// Wires the close button in the borderless dialog template (Dlg.Window in DialogStyles.xaml).
    /// The button lives in a shared template, so it can't have a Click handler of its own.
    /// </summary>
    public static class DialogChrome
    {
        public static readonly DependencyProperty ClosesWindowProperty =
            DependencyProperty.RegisterAttached("ClosesWindow", typeof(bool), typeof(DialogChrome),
                new PropertyMetadata(false, OnClosesWindowChanged));

        public static bool GetClosesWindow(DependencyObject d) => (bool)d.GetValue(ClosesWindowProperty);
        public static void SetClosesWindow(DependencyObject d, bool value) => d.SetValue(ClosesWindowProperty, value);

        private static void OnClosesWindowChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not Button button) return;
            button.Click -= Close_Click;
            if ((bool)e.NewValue) button.Click += Close_Click;
        }

        // Same as the system X: the dialog closes without a DialogResult.
        private static void Close_Click(object sender, RoutedEventArgs e) =>
            Window.GetWindow((DependencyObject)sender)?.Close();
    }
}
