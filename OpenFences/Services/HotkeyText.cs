using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;

namespace OpenFences.Services
{
    /// <summary>
    /// Shortcuts as readable text ("Ctrl+Alt+F") in settings, converted to/from what
    /// RegisterHotKey needs (modifier flags + virtual-key code).
    /// </summary>
    internal static class HotkeyText
    {
        private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_WIN = 0x8;

        /// <summary>Text for a key press, or null if it isn't a usable shortcut (a modifier is
        /// required, and the key itself can't be a modifier).</summary>
        public static string? Format(ModifierKeys mods, Key key)
        {
            if (key is Key.LeftCtrl or Key.RightCtrl or Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift
                    or Key.LWin or Key.RWin or Key.System or Key.None)
                return null;
            if ((mods & (ModifierKeys.Control | ModifierKeys.Alt | ModifierKeys.Windows)) == 0) return null;

            var parts = new List<string>();
            if (mods.HasFlag(ModifierKeys.Control)) parts.Add("Ctrl");
            if (mods.HasFlag(ModifierKeys.Alt)) parts.Add("Alt");
            if (mods.HasFlag(ModifierKeys.Shift)) parts.Add("Shift");
            if (mods.HasFlag(ModifierKeys.Windows)) parts.Add("Win");
            parts.Add(KeyName(key));
            return string.Join("+", parts);
        }

        /// <summary>Parses "Ctrl+Alt+F" into RegisterHotKey's modifiers and virtual key.</summary>
        public static bool TryParse(string? text, out uint modifiers, out uint virtualKey)
        {
            modifiers = 0;
            virtualKey = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            if (parts.Length < 2) return false;

            foreach (var p in parts.Take(parts.Length - 1))
            {
                switch (p.ToLowerInvariant())
                {
                    case "ctrl": modifiers |= MOD_CONTROL; break;
                    case "alt": modifiers |= MOD_ALT; break;
                    case "shift": modifiers |= MOD_SHIFT; break;
                    case "win": modifiers |= MOD_WIN; break;
                    default: return false;
                }
            }

            var keyText = parts[^1];
            if (keyText.Length == 1 && char.IsDigit(keyText[0])) keyText = "D" + keyText;
            if (!Enum.TryParse<Key>(keyText, ignoreCase: true, out var key)) return false;
            virtualKey = (uint)KeyInterop.VirtualKeyFromKey(key);
            return virtualKey != 0 && (modifiers & (MOD_CONTROL | MOD_ALT | MOD_WIN)) != 0;
        }

        private static string KeyName(Key key) => key switch
        {
            >= Key.D0 and <= Key.D9 => ((int)(key - Key.D0)).ToString(),
            Key.Oem3 => "OemTilde",
            _ => key.ToString()
        };
    }
}
