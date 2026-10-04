namespace OpenFences
{
    public enum FenceSort { Name, Type, DateModified, Size, Manual }
    public enum FenceIconSize { Small, Medium, Large }

    // How a background image/video fills the fence: crop to cover, show all, or stretch.
    public enum FenceBackgroundFit { Fill, Fit, Stretch }

    public class FenceModel
    {
        public string Name { get; set; } = "Fence";

        // Portals only: the real folder this fence mirrors. Normal (real-icon) fences
        // don't use a backing folder anymore — they own real desktop icons by name.
        public string FolderPath { get; set; } = "";

        // Legacy (pre-render pivot): display names of desktop icons a real fence owned by
        // physically positioning the real SysListView32 items. Kept only so old configs can be
        // migrated into ItemPaths on load; no longer used for layout.
        public System.Collections.Generic.List<string> IconNames { get; set; } = new();

        // Real (non-portal) fences: the launchable paths of the desktop items this fence owns
        // and renders as tiles. A path is a real file/folder on the desktop or a "shell:::{CLSID}"
        // moniker for a special item (This PC, Recycle Bin, …).
        public System.Collections.Generic.List<string> ItemPaths { get; set; } = new();
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 240;
        public bool Collapsed { get; set; } = false;

        // Closed with ✕: stays closed (also across restarts) until reopened from
        // View → Closed fences. It keeps owning its items, so nothing gets re-homed.
        public bool Closed { get; set; } = false;

        // Locked fences can't be moved or resized (until unlocked from the fence menu).
        public bool Locked { get; set; } = false;

        // Frosted glass: whatever is behind the fence shows through, blurred.
        public bool Glass { get; set; } = false;

        // Optional picture or video shown behind the tiles (path on disk; null = none).
        // It fades with BackgroundOpacity like the solid background does.
        public string? BackgroundMedia { get; set; }
        public FenceBackgroundFit BackgroundFit { get; set; } = FenceBackgroundFit.Fill;
        // Darkening layer over the picture so labels stay readable (0 = none … 1 = black).
        public double BackgroundDim { get; set; } = 0.35;

        // 0.0 (fully transparent) … 1.0 (opaque)
        public double BackgroundOpacity { get; set; } = 0.92;

        // A Folder Portal mirrors a real folder's live contents instead of holding
        // its own shortcuts. FolderPath then points at the mirrored folder.
        public bool IsPortal { get; set; } = false;

        public FenceSort Sort { get; set; } = FenceSort.Name;
        public FenceIconSize IconSize { get; set; } = FenceIconSize.Medium;

        // Accent color ("#RRGGBB") that tints the title bar and body; null = default graphite.
        public string? AccentColor { get; set; }

        public double TitleFontSize { get; set; } = 12;

        // Where the user last placed this fence for each monitor arrangement
        // (key: ScreenLayout.CurrentKey()), so docking/undocking puts it back.
        public System.Collections.Generic.Dictionary<string, FenceRect> Layouts { get; set; } = new();
    }

    public class FenceRect
    {
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; }
        public double Height { get; set; }
    }
}
