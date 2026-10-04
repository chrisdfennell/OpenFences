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

            [JsonIgnore] public string FilePath { get; set; } = "";
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
