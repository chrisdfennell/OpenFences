using System;
using System.Collections.Generic;
using System.Globalization;

namespace Pickets.Services
{
    /// <summary>When desktop profiles switch on their own ("Work" at 09:00 on weekdays).</summary>
    internal static class ProfileSchedule
    {
        /// <summary>Parses "9:00", "09:30" or "17:45" (24-hour).</summary>
        public static bool TryParseTime(string? text, out TimeSpan time)
        {
            time = default;
            if (string.IsNullOrWhiteSpace(text)) return false;
            return TimeSpan.TryParseExact(text.Trim(), new[] { @"h\:mm", @"hh\:mm" }, CultureInfo.InvariantCulture, out time)
                   && time < TimeSpan.FromDays(1);
        }

        /// <summary>
        /// The profile whose switch time passed after <paramref name="from"/> and up to
        /// <paramref name="to"/>; if several did (the PC was asleep), the latest one wins.
        /// </summary>
        public static DeskProfile? Due(IEnumerable<DeskProfile> profiles, DateTime from, DateTime to)
        {
            DeskProfile? best = null;
            DateTime bestAt = DateTime.MinValue;
            foreach (var p in profiles)
            {
                if (!TryParseTime(p.SwitchAt, out var t)) continue;
                for (var day = from.Date; day <= to.Date; day = day.AddDays(1))
                {
                    var at = day + t;
                    if (at <= from || at > to) continue;
                    if (p.WeekdaysOnly && day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday) continue;
                    if (at > bestAt) { best = p; bestAt = at; }
                }
            }
            return best;
        }

        /// <summary>"Switches at 9:00 on weekdays" (for menus), or "" when it doesn't.</summary>
        public static string Describe(DeskProfile p) =>
            TryParseTime(p.SwitchAt, out var t)
                ? $"at {t:h\\:mm}{(p.WeekdaysOnly ? " on weekdays" : "")}"
                : "";
    }
}
