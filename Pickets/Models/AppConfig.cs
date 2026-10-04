using System.Collections.Generic;

namespace Pickets
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

        // System-wide shortcuts: Ctrl+Alt+H hides/shows fences, Ctrl+Alt+F searches them.
        public bool GlobalHotkeys { get; set; } = true;
        public string SearchHotkey { get; set; } = "Ctrl+Alt+F";
        public string ToggleFencesHotkey { get; set; } = "Ctrl+Alt+H";

        // The "still running in the tray" notification is shown once, not on every minimize.
        public bool TrayHintShown { get; set; }

        // Where the Pickets window was and which page it showed, to reopen it the same way.
        public double? MainLeft { get; set; }
        public double? MainTop { get; set; }
        public double? MainWidth { get; set; }
        public double? MainHeight { get; set; }
        public bool MainMaximized { get; set; }
        public string? MainPage { get; set; }

        // Rolled-up (collapsed) fences open while the mouse rests on them.
        public bool ExpandCollapsedOnHover { get; set; } = true;

        // Pictures and videos show a preview instead of a generic file icon.
        public bool ShowThumbnails { get; set; } = true;

        // Tile labels leave out file extensions ("Budget" instead of "Budget.xlsx").
        public bool HideFileExtensions { get; set; } = false;
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
