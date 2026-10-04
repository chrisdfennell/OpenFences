using System;
using System.Globalization;
using System.IO;

namespace Pickets.Services
{
    /// <summary>The short size/date text a fence's list view shows next to each item.</summary>
    internal static class ItemDetails
    {
        /// <summary>"12 KB · 2:41 PM" (today), "3.4 MB · Mar 3" (this year), "Mar 3, 2024"
        /// (older); folders show just the date.</summary>
        public static string Format(bool isFolder, long size, DateTime modified, DateTime now)
        {
            var c = CultureInfo.CurrentCulture;
            string date = modified.Date == now.Date ? modified.ToString("t", c)
                        : modified.Year == now.Year ? modified.ToString("MMM d", c)
                        : modified.ToString("MMM d, yyyy", c);
            return isFolder ? date : $"{Size(size)} · {date}";
        }

        public static string Size(long bytes) => bytes switch
        {
            < 1024 => $"{bytes} B",
            < 1024 * 1024 => $"{Math.Max(1, (long)Math.Round(bytes / 1024.0))} KB", // as Explorer and Quick Look
            < 1024L * 1024 * 1024 => $"{bytes / (1024.0 * 1024):0.#} MB",
            _ => $"{bytes / (1024.0 * 1024 * 1024):0.#} GB",
        };

        /// <summary>Details for a path on disk, or "" for special items (This PC…) and missing files.</summary>
        public static string For(string path)
        {
            try
            {
                if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase)) return "";
                if (Directory.Exists(path)) return Format(true, 0, Directory.GetLastWriteTime(path), DateTime.Now);
                var f = new FileInfo(path);
                return f.Exists ? Format(false, f.Length, f.LastWriteTime, DateTime.Now) : "";
            }
            catch { return ""; }
        }
    }
}
