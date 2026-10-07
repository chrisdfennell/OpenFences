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

        // Fences snapped one under another stay together: rolling one up pulls the ones below it
        // up, and Alt-dragging a title bar moves the whole stack (Services/FenceStacks).
        public bool MoveStacksTogether { get; set; } = true;
        // With stacks on, a plain title-bar drag moves the whole stack (and Alt-drag one fence)
        // instead of the other way round.
        public bool DragStacksTogether { get; set; } = false;
        // In a stack, opening one fence rolls the others up.
        public bool StackOneOpen { get; set; } = false;

        // System-wide shortcuts: Ctrl+Alt+H hides/shows fences, Ctrl+Alt+F searches them.
        public bool GlobalHotkeys { get; set; } = true;
        public string SearchHotkey { get; set; } = "Ctrl+Alt+F";
        public string ToggleFencesHotkey { get; set; } = "Ctrl+Alt+H";
        // Brings every fence in front of app windows until you click elsewhere (Services/FencesInFront).
        public string FrontHotkey { get; set; } = "Ctrl+Alt+Space";
        // Switches to the next desktop profile.
        public string ProfileHotkey { get; set; } = "Ctrl+Alt+P";

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

        // System monitor fences: how often they read (seconds), how much history the graphs
        // show (minutes), network speed in bits (Mbps) instead of bytes (MB/s), and amber/red
        // readings when something is busy, nearly full or low.
        public int MonitorIntervalSeconds { get; set; } = 2;
        public int MonitorHistoryMinutes { get; set; } = 2;
        public bool MonitorNetworkBits { get; set; } = false;
        public bool MonitorWarnColors { get; set; } = true;
        // Network info also shows the public IP, which means asking a service on the internet
        // (see SystemMonitor.PublicIpService and the privacy policy), so it's off unless turned on.
        public bool MonitorPublicIp { get; set; } = false;

        // Light or dark look; System follows Windows' app mode.
        public Pickets.Services.AppTheme Theme { get; set; } = Pickets.Services.AppTheme.System;
    }

    // How a desktop item is matched to a target fence by the rules engine.
    public enum RuleKind { Executable, Folder, Extensions, Any, NamePattern, OlderThan, LargerThan }

    public class FenceRule
    {
        public RuleKind Kind { get; set; } = RuleKind.Any;

        // Only used when Kind == Extensions (e.g. ".png", ".jpg"). Case-insensitive.
        public List<string> Extensions { get; set; } = new();

        // Kind == NamePattern: wildcard names, e.g. "invoice* screenshot*" (see Services/PortalFilter).
        public string Pattern { get; set; } = "";

        // Kind == OlderThan: days since last change. Kind == LargerThan: size in MB.
        public double Amount { get; set; }

        // Name of the fence to route matching items into (created if missing).
        public string TargetFence { get; set; } = "Documents";
    }

    /// <summary>A named set of fences ("Work", "Home"). The active profile's fences are
    /// AppConfig.Fences; the others wait here until switched to.</summary>
    public class DeskProfile
    {
        public string Name { get; set; } = "Profile";
        public List<FenceModel> Fences { get; set; } = new();

        // Switch to this profile when the clock reaches this time ("HH:mm"); null = never.
        public string? SwitchAt { get; set; }
        public bool WeekdaysOnly { get; set; }
    }

    public class AppConfig
    {
        public List<FenceModel> Fences { get; set; } = new();
        public AppOptions Options { get; set; } = new();

        // Empty until the first profile is made; ActiveProfile names the one whose fences are shown.
        public List<DeskProfile> Profiles { get; set; } = new();
        public string? ActiveProfile { get; set; }

        // Evaluated top-to-bottom; first match wins. Defaults mirror the one-shot import.
        public List<FenceRule> Rules { get; set; } = new()
        {
            new FenceRule { Kind = RuleKind.Executable, TargetFence = "Apps" },
            new FenceRule { Kind = RuleKind.Any,        TargetFence = "Documents" },
        };
    }
}
