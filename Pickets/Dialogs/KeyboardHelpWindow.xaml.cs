using System.Windows;
using System.Windows.Controls;

namespace Pickets
{
    /// <summary>F1: every keyboard shortcut (and the mouse tricks that go with them), including
    /// the global ones as currently set in Settings.</summary>
    public partial class KeyboardHelpWindow : Window
    {
        public KeyboardHelpWindow(AppOptions options)
        {
            InitializeComponent();
            Pickets.Services.DarkTitleBar.Apply(this);

            string off = options.GlobalHotkeys ? "" : " (turned off in Settings)";
            AddSection("Anywhere" + off,
                (options.ToggleFencesHotkey, "Hide or show all fences"),
                (options.SearchHotkey, "Search every fence"),
                (options.FrontHotkey, "Bring fences in front of your windows (Esc or a click elsewhere sends them back)"),
                (options.ProfileHotkey, "Switch to the next desktop profile"));

            AddSection("In a fence",
                ("Arrow keys, Home, End", "Move between items"),
                ("Shift + arrow keys", "Select more items"),
                ("A letter or number", "Jump to the next item starting with it"),
                ("Ctrl+A", "Select everything in the fence"),
                ("Enter", "Open the selected items"),
                ("Space", "Quick Look: a large preview"),
                ("F2", "Rename the selected item (or the fence, with nothing selected)"),
                ("Delete", "Move the selected items to the Recycle Bin"),
                ("Ctrl+Z", "Undo the last move, removal, tab change or deleted fence"),
                ("Ctrl+Tab, Ctrl+Shift+Tab", "Next or previous tab"),
                ("Ctrl + mouse wheel", "Bigger or smaller icons"),
                ("Backspace, Alt+Left", "Up one folder in a folder portal"),
                ("Esc", "Clear the selection"),
                ("F1", "This list"));

            AddSection("Quick Look",
                ("Left, Right, Up, Down", "Previous or next item in the fence"),
                ("Enter", "Open the item"),
                ("Esc, Space", "Close the preview"));

            AddSection("With the mouse",
                ("Double-click a title", "Roll the fence up, or open it again"),
                ("Drag a title", "Move the fence (and the fences stacked with it)"),
                ("Alt while dragging", "Move freely: no snapping, and out of its stack"),
                ("Ctrl+double-click", "Open a folder in a portal in File Explorer instead"),
                ("Drag on the desktop", "Select items across fences (right-drag draws a new fence)"));
        }

        private void AddSection(string title, params (string Keys, string What)[] rows)
        {
            Sections.Children.Add(new TextBlock
            {
                Text = title,
                FontSize = 15,
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, Sections.Children.Count == 0 ? 4 : 18, 0, 6)
            });

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(200) });
            grid.ColumnDefinitions.Add(new ColumnDefinition());
            foreach (var (keys, what) in rows)
            {
                int row = grid.RowDefinitions.Count;
                grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

                var keyCap = new Border
                {
                    Child = new TextBlock { Text = keys, FontWeight = FontWeights.SemiBold, TextWrapping = TextWrapping.Wrap },
                    Padding = new Thickness(8, 3, 8, 3),
                    Margin = new Thickness(0, 3, 12, 3),
                    CornerRadius = new CornerRadius(5),
                    BorderThickness = new Thickness(1),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    VerticalAlignment = System.Windows.VerticalAlignment.Top
                };
                keyCap.SetResourceReference(Border.BackgroundProperty, "Dlg.Card");
                keyCap.SetResourceReference(Border.BorderBrushProperty, "Dlg.CardEdge");
                Grid.SetRow(keyCap, row);
                grid.Children.Add(keyCap);

                var text = new TextBlock { Text = what, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 6, 0, 3) };
                text.SetResourceReference(TextBlock.ForegroundProperty, "Dlg.Body");
                Grid.SetRow(text, row);
                Grid.SetColumn(text, 1);
                grid.Children.Add(text);
            }
            Sections.Children.Add(grid);
        }

        private void Close_Click(object sender, RoutedEventArgs e) => Close();
    }
}
