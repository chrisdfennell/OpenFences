using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace OpenFences.Services
{
    /// <summary>
    /// Classifies desktop items and maps them to a target fence using the user's rules.
    /// Shared by the one-shot Auto-Import and the continuous auto-organize watcher.
    /// </summary>
    public static class DesktopRules
    {
        public static bool IsExecutableTarget(string? targetPath)
        {
            if (string.IsNullOrWhiteSpace(targetPath)) return false;
            string ext = Path.GetExtension(targetPath).ToLowerInvariant();
            return ext is ".exe" or ".bat" or ".cmd" or ".ps1" or ".msi" or ".appref-ms";
        }

        public static bool IsDocumentExtension(string ext)
        {
            return new[]
            {
                ".txt",".md",".rtf",".pdf",
                ".doc",".docx",".odt",
                ".xls",".xlsx",".csv",
                ".ppt",".pptx",
                ".png",".jpg",".jpeg",".gif",".bmp",".webp",
                ".json",".xml",".zip",".7z",".rar"
            }.Contains(ext);
        }

        /// <summary>True if the item is (or points at) a runnable application.</summary>
        public static bool IsExecutableItem(string path)
        {
            string ext = Path.GetExtension(path).ToLowerInvariant();
            if (ext is ".exe" or ".bat" or ".cmd" or ".ps1" or ".msi" or ".appref-ms" or ".url")
                return true;

            if (ext == ".lnk")
            {
                try { return IsExecutableTarget(ShellLink.GetShortcutTarget(path)); }
                catch { return false; }
            }
            return false;
        }

        /// <summary>
        /// Returns the name of the fence the item should land in, or null if no rule matches.
        /// Rules are evaluated top-to-bottom; first match wins.
        /// </summary>
        public static string? ResolveTargetFence(string path, IEnumerable<FenceRule> rules)
        {
            bool isDir = Directory.Exists(path);
            string ext = Path.GetExtension(path).ToLowerInvariant();

            foreach (var rule in rules)
            {
                bool match = rule.Kind switch
                {
                    RuleKind.Executable => !isDir && IsExecutableItem(path),
                    RuleKind.Folder => isDir,
                    RuleKind.Extensions => !isDir && rule.Extensions
                        .Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)),
                    RuleKind.Any => true,
                    _ => false
                };

                if (match) return rule.TargetFence;
            }
            return null;
        }
    }
}
