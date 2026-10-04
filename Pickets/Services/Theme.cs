using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;
using SystemColors = System.Windows.SystemColors;

namespace Pickets.Services
{
    public enum AppTheme { System, Light, Dark }

    /// <summary>Which Windows system color a palette entry takes under a high-contrast theme.</summary>
    public enum ContrastRole { Window, WindowText, Highlight, HighlightText, ButtonFace, ButtonText }

    /// <summary>
    /// Light or dark look for Pickets' own windows, menus and default fences. "System" follows
    /// Windows' app mode (Settings → Personalization → Colors) and switches live when it changes.
    /// Swaps Themes/Palette.*.xaml in the application resources, so XAML uses DynamicResource.
    /// Under a Windows high-contrast theme every palette color is replaced by the theme's own
    /// system colors instead, whatever the choice.
    /// </summary>
    internal static class Theme
    {
        private const string PalettePrefix = "Themes/Palette.";

        public static AppTheme Choice { get; private set; } = AppTheme.System;
        public static bool IsLight { get; private set; }
        public static bool IsHighContrast { get; private set; }

        /// <summary>Raised after the palette changes, for things painted in code (fences, title bars).</summary>
        public static event Action? Changed;

        private static bool _listening;
        private static ResourceDictionary? _palette;

        public static void Apply(AppTheme choice)
        {
            Choice = choice;
            if (!_listening)
            {
                _listening = true;
                SystemEvents.UserPreferenceChanged += (_, e) =>
                {
                    if ((e.Category == UserPreferenceCategory.General && Choice == AppTheme.System) ||
                        e.Category is UserPreferenceCategory.Accessibility or UserPreferenceCategory.Color)
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);
                };
                SystemParameters.StaticPropertyChanged += (_, e) =>
                {
                    if (e.PropertyName == nameof(SystemParameters.HighContrast))
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);
                };
            }
            Refresh();
        }

        private static void Refresh()
        {
            bool contrast = SystemParameters.HighContrast;
            bool light = contrast ? IsLightColor(SystemColors.WindowColor) : Choice switch
            {
                AppTheme.Light => true,
                AppTheme.Dark => false,
                _ => WindowsUsesLightApps(),
            };

            var app = System.Windows.Application.Current;
            if (app == null) return;
            var dicts = app.Resources.MergedDictionaries;
            _palette ??= dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains(PalettePrefix) == true);
            // A high-contrast palette is rebuilt every time: switching between contrast themes
            // changes the system colors without changing anything else.
            if (_palette != null && light == IsLight && contrast == IsHighContrast && !contrast) return;

            IsLight = light;
            IsHighContrast = contrast;
            var palette = contrast ? ContrastPalette() : LoadPalette(light);
            if (_palette != null && dicts.Contains(_palette)) dicts[dicts.IndexOf(_palette)] = palette;
            else dicts.Insert(0, palette);
            _palette = palette;

            Changed?.Invoke();
        }

        private static ResourceDictionary LoadPalette(bool light) => new()
        {
            Source = new Uri("pack://application:,,,/Pickets;component/" + PalettePrefix + (light ? "Light" : "Dark") + ".xaml")
        };

        // Same keys as the normal palettes, each set to the system color for its role.
        private static ResourceDictionary ContrastPalette()
        {
            var palette = new ResourceDictionary();
            foreach (var key in LoadPalette(light: false).Keys.OfType<string>())
                palette[key] = ContrastBrush(RoleFor(key));
            return palette;
        }

        public static System.Windows.Media.SolidColorBrush ContrastBrush(ContrastRole role)
        {
            var brush = new System.Windows.Media.SolidColorBrush(role switch
            {
                ContrastRole.WindowText => SystemColors.WindowTextColor,
                ContrastRole.Highlight => SystemColors.HighlightColor,
                ContrastRole.HighlightText => SystemColors.HighlightTextColor,
                ContrastRole.ButtonFace => SystemColors.ControlColor,
                ContrastRole.ButtonText => SystemColors.ControlTextColor,
                _ => SystemColors.WindowColor,
            });
            brush.Freeze();
            return brush;
        }

        /// <summary>The system color a palette key ("Hub.Muted", "Menu.ItemHover"…) takes under
        /// high contrast: backgrounds the window color, text and edges the text color, selection
        /// and accents the highlight color.</summary>
        internal static ContrastRole RoleFor(string key)
        {
            var name = key[(key.IndexOf('.') + 1)..];
            switch (name)
            {
                case "OnAccent": return ContrastRole.HighlightText;
                case "Accent": case "AccentHover": case "Selected": case "Primary": case "PrimaryEdge":
                case "ItemHover": case "ItemPress":
                    return ContrastRole.Highlight;
                case "BtnBg": case "BtnHover": case "BtnPress": return ContrastRole.ButtonFace;
                case "BtnFg": case "BtnBorder": return ContrastRole.ButtonText;
            }
            if (name.EndsWith("Edge") || name.Contains("Border") || name == "Separator" || name.EndsWith("Thumb"))
                return ContrastRole.WindowText;
            string[] text = { "Text", "Fg", "Foreground", "Strong", "Body", "Muted", "Subtle", "Hint", "Where",
                              "Code", "Danger", "Warning", "Swatch" };
            return text.Any(name.EndsWith) ? ContrastRole.WindowText : ContrastRole.Window;
        }

        private static bool IsLightColor(System.Windows.Media.Color c) => 0.299 * c.R + 0.587 * c.G + 0.114 * c.B > 150;

        private static bool WindowsUsesLightApps()
        {
            try
            {
                using var key = Registry.CurrentUser.OpenSubKey(
                    @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
                return key?.GetValue("AppsUseLightTheme") is int v && v != 0;
            }
            catch { return false; }
        }
    }
}
