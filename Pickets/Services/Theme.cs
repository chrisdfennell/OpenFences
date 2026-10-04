using System;
using System.Linq;
using System.Windows;
using Microsoft.Win32;

namespace Pickets.Services
{
    public enum AppTheme { System, Light, Dark }

    /// <summary>
    /// Light or dark look for Pickets' own windows, menus and default fences. "System" follows
    /// Windows' app mode (Settings → Personalization → Colors) and switches live when it changes.
    /// Swaps Themes/Palette.*.xaml in the application resources, so XAML uses DynamicResource.
    /// </summary>
    internal static class Theme
    {
        private const string PalettePrefix = "Themes/Palette.";

        public static AppTheme Choice { get; private set; } = AppTheme.System;
        public static bool IsLight { get; private set; }

        /// <summary>Raised after the palette changes, for things painted in code (fences, title bars).</summary>
        public static event Action? Changed;

        private static bool _listening;

        public static void Apply(AppTheme choice)
        {
            Choice = choice;
            if (!_listening)
            {
                _listening = true;
                SystemEvents.UserPreferenceChanged += (_, e) =>
                {
                    if (e.Category == UserPreferenceCategory.General && Choice == AppTheme.System)
                        System.Windows.Application.Current?.Dispatcher.BeginInvoke(Refresh);
                };
            }
            Refresh();
        }

        private static void Refresh()
        {
            bool light = Choice switch
            {
                AppTheme.Light => true,
                AppTheme.Dark => false,
                _ => WindowsUsesLightApps(),
            };

            var app = System.Windows.Application.Current;
            if (app == null) return;
            var dicts = app.Resources.MergedDictionaries;
            var current = dicts.FirstOrDefault(d => d.Source?.OriginalString.Contains(PalettePrefix) == true);
            if (current != null && light == IsLight) return;

            IsLight = light;
            var palette = new ResourceDictionary
            {
                Source = new Uri("pack://application:,,,/Pickets;component/" + PalettePrefix + (light ? "Light" : "Dark") + ".xaml")
            };
            if (current != null) dicts[dicts.IndexOf(current)] = palette;
            else dicts.Insert(0, palette);

            Changed?.Invoke();
        }

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
