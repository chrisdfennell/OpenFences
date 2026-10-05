using System.Collections.Generic;
using System.Linq;
using System.Media;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Application = System.Windows.Application;
using Brush = System.Windows.Media.Brush;
using Button = System.Windows.Controls.Button;
using Clipboard = System.Windows.Clipboard;
using Color = System.Windows.Media.Color;
using KeyEventArgs = System.Windows.Input.KeyEventArgs;

namespace Pickets
{
    /// <summary>
    /// Drop-in for System.Windows.MessageBox that uses the app's borderless dialog look
    /// instead of the stock Windows box. Files alias it with
    /// <c>using MessageBox = Pickets.ThemedMessageBox;</c>.
    /// </summary>
    public static class ThemedMessageBox
    {
        public static MessageBoxResult Show(string text, string caption = "Pickets",
            MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
            MessageBoxResult defaultResult = MessageBoxResult.None)
            => Show(null, text, caption, button, icon, defaultResult);

        /// <param name="labels">Optional button text by result (e.g. Yes → "Minimize to tray"); others keep their default text.</param>
        public static MessageBoxResult Show(Window? owner, string text, string caption = "Pickets",
            MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None,
            MessageBoxResult defaultResult = MessageBoxResult.None,
            IReadOnlyDictionary<MessageBoxResult, string>? labels = null)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
                return dispatcher.Invoke(() => Show(owner, text, caption, button, icon, defaultResult, labels));

            var dialog = new MessageDialog(text, caption, button, icon, defaultResult, labels);

            // Like the stock box: owned by whichever app window is active, if any.
            owner ??= Application.Current?.Windows.OfType<Window>()
                .FirstOrDefault(w => w.IsActive && w.IsVisible && w != dialog);
            if (owner is { IsVisible: true })
            {
                dialog.Owner = owner;
                dialog.WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            dialog.ShowDialog();
            return dialog.Result;
        }
    }

    public partial class MessageDialog : Window
    {
        private readonly string _text;
        private readonly string _caption;

        /// <summary>What was clicked; closing with the X or Alt+F4 counts as Cancel (or No, or OK).</summary>
        public MessageBoxResult Result { get; private set; }

        internal MessageDialog(string text, string caption, MessageBoxButton button,
            MessageBoxImage icon, MessageBoxResult defaultResult,
            IReadOnlyDictionary<MessageBoxResult, string>? labels = null)
        {
            InitializeComponent();
            _text = text ?? "";
            _caption = string.IsNullOrEmpty(caption) ? "Pickets" : caption;

            Title = _caption;
            CaptionText.Text = _caption;
            MessageText.Text = _text;
            SetIcon(icon);

            var results = button switch
            {
                MessageBoxButton.OKCancel => new[] { MessageBoxResult.OK, MessageBoxResult.Cancel },
                MessageBoxButton.YesNo => new[] { MessageBoxResult.Yes, MessageBoxResult.No },
                MessageBoxButton.YesNoCancel => new[] { MessageBoxResult.Yes, MessageBoxResult.No, MessageBoxResult.Cancel },
                _ => new[] { MessageBoxResult.OK },
            };
            var dismiss = results.Contains(MessageBoxResult.Cancel) ? MessageBoxResult.Cancel
                        : results.Contains(MessageBoxResult.No) ? MessageBoxResult.No
                        : MessageBoxResult.OK;
            var preferred = results.Contains(defaultResult) ? defaultResult : results[0];
            Result = dismiss;

            Button? focus = null;
            foreach (var r in results)
            {
                var b = new Button
                {
                    Content = labels != null && labels.TryGetValue(r, out var label) ? label : r.ToString(),
                    MinWidth = 96,
                    Margin = new Thickness(8, 0, 0, 0),
                    IsDefault = r == preferred,
                    IsCancel = r == dismiss,
                };
                if (r == preferred)
                {
                    b.Style = (Style)FindResource("PrimaryButton");
                    // Narrator reads the focused button on open; give it the message too.
                    AutomationProperties.SetHelpText(b, _text);
                    focus = b;
                }
                var result = r;
                b.Click += (_, __) => { Result = result; Close(); };
                ButtonRow.Children.Add(b);
            }

            AutomationProperties.SetHelpText(this, _text);
            Loaded += (_, __) =>
            {
                focus?.Focus();
                PlaySound(icon);
            };
        }

        protected override void OnPreviewKeyDown(KeyEventArgs e)
        {
            // Ctrl+C copies the message, as it does in the stock box (handy for error reports).
            if (e.Key == Key.C && Keyboard.Modifiers == ModifierKeys.Control)
            {
                try { Clipboard.SetText(_caption + "\r\n\r\n" + _text); } catch { /* clipboard busy */ }
                e.Handled = true;
                return;
            }
            base.OnPreviewKeyDown(e);
        }

        private void SetIcon(MessageBoxImage icon)
        {
            // MessageBoxImage aliases: Hand = Stop = Error, Exclamation = Warning, Asterisk = Information.
            (string glyph, Brush brush)? look = icon switch
            {
                MessageBoxImage.Error => ("", new SolidColorBrush(Color.FromRgb(0xE5, 0x48, 0x4D))),
                MessageBoxImage.Warning => ("", new SolidColorBrush(Color.FromRgb(0xF5, 0xA5, 0x24))),
                MessageBoxImage.Information => ("", (Brush)FindResource("Dlg.Accent")),
                MessageBoxImage.Question => ("", (Brush)FindResource("Dlg.Accent")),
                _ => null,
            };
            if (look is not { } l) return;
            IconGlyph.Text = l.glyph;
            IconGlyph.Foreground = l.brush;
            IconGlyph.Visibility = Visibility.Visible;
        }

        private static void PlaySound(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Error: SystemSounds.Hand.Play(); break;
                case MessageBoxImage.Warning: SystemSounds.Exclamation.Play(); break;
                case MessageBoxImage.Information: SystemSounds.Asterisk.Play(); break;
                case MessageBoxImage.Question: SystemSounds.Question.Play(); break;
            }
        }
    }
}
