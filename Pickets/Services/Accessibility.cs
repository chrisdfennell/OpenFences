using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Button = System.Windows.Controls.Button;
using ButtonBase = System.Windows.Controls.Primitives.ButtonBase;
using Control = System.Windows.Controls.Control;
using Panel = System.Windows.Controls.Panel;

namespace Pickets.Services
{
    /// <summary>
    /// Screen reader help: names for controls that would otherwise be read as a glyph or as
    /// nothing at all (icon buttons, settings switches), and spoken announcements for things
    /// that change without focus moving (undo, rolling a fence up, switching tabs).
    /// </summary>
    internal static class Accessibility
    {
        /// <summary>True for text a screen reader can't say usefully: icon-font glyphs (private
        /// use area), symbols such as ✎ ✕, and punctuation such as – or ‹.</summary>
        public static bool IsSymbolOnly(string? text) =>
            !string.IsNullOrWhiteSpace(text) &&
            text.All(c => char.IsWhiteSpace(c) || (c >= '' && c <= '') ||
                          char.IsSymbol(c) || char.IsPunctuation(c) || char.IsSurrogate(c));

        /// <summary>
        /// Gives a name to every button under <paramref name="root"/> that has none worth reading:
        /// its text if it has some next to a glyph, otherwise its tooltip. Every control in a
        /// settings row (<paramref name="settingRowStyle"/>) is named after the row's title, with
        /// the row's description as help text. Controls with a name already are left alone.
        /// </summary>
        public static void NameControls(DependencyObject root, Style? settingRowStyle)
        {
            foreach (var el in LogicalDescendants(root))
            {
                if (el is DockPanel row && settingRowStyle != null && row.Style == settingRowStyle)
                {
                    var texts = row.Children.OfType<StackPanel>().FirstOrDefault()?.Children.OfType<TextBlock>().ToList();
                    if (texts == null || texts.Count == 0) continue;
                    foreach (var control in row.Children.OfType<Control>().Where(c => c is not Button))
                    {
                        if (!string.IsNullOrEmpty(AutomationProperties.GetName(control))) continue;
                        AutomationProperties.SetName(control, texts[0].Text);
                        if (texts.Count > 1) AutomationProperties.SetHelpText(control, texts[1].Text);
                    }
                }
                else if (el is ButtonBase b && string.IsNullOrEmpty(AutomationProperties.GetName(b)))
                {
                    var name = b.Content is string s ? (IsSymbolOnly(s) ? b.ToolTip as string : null)
                                                     : TextOf(b.Content) ?? b.ToolTip as string;
                    if (!string.IsNullOrWhiteSpace(name)) AutomationProperties.SetName(b, name);
                }
            }
        }

        /// <summary>Have a screen reader say <paramref name="message"/> (through a live region:
        /// a TextBlock with AutomationProperties.LiveSetting set).</summary>
        public static void Announce(TextBlock liveRegion, string message)
        {
            liveRegion.Text = message;
            UIElementAutomationPeer.CreatePeerForElement(liveRegion)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
        }

        // The readable text inside a button's content, skipping glyphs.
        private static string? TextOf(object? content)
        {
            var parts = new List<string>();
            void Collect(object? o)
            {
                switch (o)
                {
                    case string s when !IsSymbolOnly(s): parts.Add(s); break;
                    case TextBlock t when !IsSymbolOnly(t.Text): parts.Add(t.Text); break;
                    case Panel p: foreach (var c in p.Children) Collect(c); break;
                    case Decorator d: Collect(d.Child); break;
                }
            }
            Collect(content);
            return parts.Count > 0 ? string.Join(" ", parts) : null;
        }

        private static IEnumerable<DependencyObject> LogicalDescendants(DependencyObject root)
        {
            foreach (var child in LogicalTreeHelper.GetChildren(root).OfType<DependencyObject>())
            {
                yield return child;
                foreach (var d in LogicalDescendants(child)) yield return d;
            }
        }
    }
}
