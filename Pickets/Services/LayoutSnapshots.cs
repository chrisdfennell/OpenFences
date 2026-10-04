using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Pickets.Services
{
    /// <summary>
    /// Saved copies of the whole fence layout (positions, sizes, items, styling), stored as
    /// %AppData%\Pickets\layouts\*.json. Automatic snapshots are taken before risky actions
    /// and only the most recent few are kept.
    /// </summary>
    internal static class LayoutSnapshots
    {
        public sealed class Snapshot
        {
            public string Name { get; set; } = "";
            public DateTime Created { get; set; }
            public bool Automatic { get; set; }
            public List<FenceModel> Fences { get; set; } = new();

            // Exported files only (null in ordinary snapshots): the rules, and where the
            // exporting PC kept the user's folders, so paths can be moved to this PC's.
            public List<FenceRule>? Rules { get; set; }
            public string? DesktopFolder { get; set; }
            public string? ProfileFolder { get; set; }

            [JsonIgnore] public string FilePath { get; set; } = "";
        }

        private static string CurrentDesktop => Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        private static string CurrentProfile => Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);

        /// <summary>Write the whole setup (fences and rules) to a file the user picked.</summary>
        public static void Export(string file, IEnumerable<FenceModel> fences, IEnumerable<FenceRule> rules)
        {
            var snap = new Snapshot
            {
                Name = Path.GetFileNameWithoutExtension(file),
                Created = DateTime.Now,
                Fences = Clone(fences),
                Rules = rules.ToList(),
                DesktopFolder = CurrentDesktop,
                ProfileFolder = CurrentProfile,
            };
            File.WriteAllText(file, JsonSerializer.Serialize(snap, JsonOpts));
        }

        /// <summary>Read an exported (or snapshot) file, with its paths moved to this PC's
        /// desktop and profile folders. Throws if it isn't a Pickets layout.</summary>
        public static Snapshot Import(string file)
        {
            var snap = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file), JsonOpts);
            if (snap == null || snap.Fences.Count == 0)
                throw new InvalidDataException("This file doesn't contain a Pickets layout.");
            if (string.IsNullOrWhiteSpace(snap.Name)) snap.Name = Path.GetFileNameWithoutExtension(file);

            // The desktop first: it may live outside the profile (OneDrive, another drive).
            RemapPaths(snap.Fences, snap.DesktopFolder, CurrentDesktop);
            RemapPaths(snap.Fences, snap.ProfileFolder, CurrentProfile);
            return snap;
        }

        /// <summary>Replace the <paramref name="from"/> folder prefix with <paramref name="to"/> in
        /// every path a fence stores (items, tabs, portal folder, background).</summary>
        internal static void RemapPaths(IEnumerable<FenceModel> fences, string? from, string to)
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) return;
            from = from.TrimEnd('\\');
            to = to.TrimEnd('\\');
            if (string.Equals(from, to, StringComparison.OrdinalIgnoreCase)) return;

            string Map(string p) =>
                p.StartsWith(from + "\\", StringComparison.OrdinalIgnoreCase) || string.Equals(p, from, StringComparison.OrdinalIgnoreCase)
                    ? to + p.Substring(from.Length)
                    : p;

            foreach (var f in fences)
            {
                f.ItemPaths = f.ItemPaths.Select(Map).ToList();
                foreach (var t in f.Tabs) t.ItemPaths = t.ItemPaths.Select(Map).ToList();
                if (!string.IsNullOrEmpty(f.FolderPath)) f.FolderPath = Map(f.FolderPath);
                if (!string.IsNullOrEmpty(f.BackgroundMedia)) f.BackgroundMedia = Map(f.BackgroundMedia);
            }
        }

        private const int KeepAutomatic = 10;

        private static readonly JsonSerializerOptions JsonOpts = new()
        {
            WriteIndented = true,
            Converters = { new JsonStringEnumConverter() }
        };

        public static string Folder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Pickets", "layouts");

        /// <summary>Newest first. Unreadable files are skipped.</summary>
        public static List<Snapshot> List()
        {
            var list = new List<Snapshot>();
            if (!Directory.Exists(Folder)) return list;
            foreach (var file in Directory.EnumerateFiles(Folder, "*.json"))
            {
                try
                {
                    var snap = JsonSerializer.Deserialize<Snapshot>(File.ReadAllText(file), JsonOpts);
                    if (snap == null) continue;
                    snap.FilePath = file;
                    list.Add(snap);
                }
                catch { /* skip damaged snapshot */ }
            }
            return list.OrderByDescending(s => s.Created).ToList();
        }

        public static void Save(string name, IEnumerable<FenceModel> fences, bool automatic)
        {
            Directory.CreateDirectory(Folder);
            var snap = new Snapshot
            {
                Name = name,
                Created = DateTime.Now,
                Automatic = automatic,
                Fences = Clone(fences)
            };
            var file = Path.Combine(Folder, $"{snap.Created:yyyyMMdd-HHmmss-fff}{(automatic ? "-auto" : "")}.json");
            File.WriteAllText(file, JsonSerializer.Serialize(snap, JsonOpts));

            if (automatic) PruneAutomatic();
        }

        public static void Delete(Snapshot snap)
        {
            try { File.Delete(snap.FilePath); } catch { /* ignore */ }
        }

        /// <summary>Deep copy via JSON, so a snapshot never shares objects with the live layout.</summary>
        public static List<FenceModel> Clone(IEnumerable<FenceModel> fences) =>
            JsonSerializer.Deserialize<List<FenceModel>>(JsonSerializer.Serialize(fences.ToList(), JsonOpts), JsonOpts)
            ?? new List<FenceModel>();

        private static void PruneAutomatic()
        {
            foreach (var old in List().Where(s => s.Automatic).Skip(KeepAutomatic))
                Delete(old);
        }
    }
}
