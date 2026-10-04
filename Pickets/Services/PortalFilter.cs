using System;
using System.Collections.Generic;
using System.IO.Enumeration;
using System.Linq;

namespace Pickets.Services
{
    /// <summary>
    /// What a filtered folder portal shows: files whose names match any of the patterns, and/or
    /// items changed within the last N days. Name patterns are for files, so a portal with
    /// patterns hides its subfolders; the age limit applies to files and folders alike.
    /// </summary>
    internal static class PortalFilter
    {
        /// <summary>Choices offered for "Changed within" (days; null = any time).</summary>
        public static readonly (int? Days, string Label)[] AgeChoices =
        {
            (null, "Any time"),
            (1, "Last 24 hours"),
            (7, "Last 7 days"),
            (30, "Last 30 days"),
            (90, "Last 90 days"),
            (365, "Last year"),
        };

        public static string AgeLabel(int? days) =>
            AgeChoices.FirstOrDefault(c => c.Days == days).Label ?? $"Last {days} days";

        /// <summary>
        /// Splits "*.pdf; .docx invoice" into wildcard patterns. ".docx" means "*.docx", and a
        /// word without wildcards matches anywhere in the name ("invoice" → "*invoice*").
        /// </summary>
        public static List<string> ParsePatterns(string? text)
        {
            var result = new List<string>();
            if (string.IsNullOrWhiteSpace(text)) return result;

            foreach (var raw in text.Split(new[] { ';', ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = raw.Trim();
                if (p.Length == 0) continue;
                if (p.IndexOfAny(new[] { '*', '?' }) < 0)
                    p = p.StartsWith('.') ? "*" + p : "*" + p + "*";
                if (!result.Contains(p, StringComparer.OrdinalIgnoreCase)) result.Add(p);
            }
            return result;
        }

        public static bool IsActive(string? patterns, int? maxAgeDays) =>
            ParsePatterns(patterns).Count > 0 || maxAgeDays is > 0;

        public static bool Matches(string name, bool isFolder, DateTime lastWriteUtc,
                                   IReadOnlyList<string> patterns, int? maxAgeDays, DateTime nowUtc)
        {
            if (maxAgeDays is > 0 && lastWriteUtc < nowUtc.AddDays(-maxAgeDays.Value)) return false;
            if (patterns.Count == 0) return true;
            if (isFolder) return false;
            return patterns.Any(p => FileSystemName.MatchesSimpleExpression(p, name, ignoreCase: true));
        }

        /// <summary>One-line description for the fence's tooltip, e.g. "*.pdf, *.docx · Last 7 days".</summary>
        public static string Describe(string? patterns, int? maxAgeDays)
        {
            var parts = new List<string>();
            var list = ParsePatterns(patterns);
            if (list.Count > 0) parts.Add(string.Join(", ", list));
            if (maxAgeDays is > 0) parts.Add(AgeLabel(maxAgeDays));
            return string.Join(" · ", parts);
        }
    }
}
