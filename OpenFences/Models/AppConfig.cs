using System.Collections.Generic;

namespace OpenFences
{
    public class AppOptions
    {
        public bool RunAtStartup { get; set; } = false;
        public bool HideIconsOnStartup { get; set; } = false;
        public bool DoubleClickDesktopToToggleIcons { get; set; } = true;

        // Continuously route newly added desktop items into fences using Rules.
        public bool AutoOrganize { get; set; } = false;

        // Double-clicking empty desktop hides/shows all fences ("peek").
        public bool DoubleClickPeekFences { get; set; } = false;

        // Check GitHub for a newer release at startup and once a day.
        public bool CheckForUpdates { get; set; } = true;

        // A version the user chose "Skip this version" for; automatic checks stay quiet about it.
        public string? SkippedVersion { get; set; }

        // While moving/resizing, fences snap to screen edges and to each other (Alt = free)…
        public bool SnapToEdges { get; set; } = true;
        // …and optionally to a 20px grid.
        public bool SnapToGrid { get; set; } = false;
    }

    // How a desktop item is matched to a target fence by the rules engine.
    public enum RuleKind { Executable, Folder, Extensions, Any }

    public class FenceRule
    {
        public RuleKind Kind { get; set; } = RuleKind.Any;

        // Only used when Kind == Extensions (e.g. ".png", ".jpg"). Case-insensitive.
        public List<string> Extensions { get; set; } = new();

        // Name of the fence to route matching items into (created if missing).
        public string TargetFence { get; set; } = "Documents";
    }

    public class AppConfig
    {
        public List<FenceModel> Fences { get; set; } = new();
        public AppOptions Options { get; set; } = new();

        // Evaluated top-to-bottom; first match wins. Defaults mirror the one-shot import.
        public List<FenceRule> Rules { get; set; } = new()
        {
            new FenceRule { Kind = RuleKind.Executable, TargetFence = "Apps" },
            new FenceRule { Kind = RuleKind.Any,        TargetFence = "Documents" },
        };
    }
}
