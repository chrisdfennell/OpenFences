namespace OpenFences
{
    public enum FenceSort { Name, Type, DateModified, Size, Manual }
    public enum FenceIconSize { Small, Medium, Large }

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

        // 0.0 (fully transparent) … 1.0 (opaque)
        public double BackgroundOpacity { get; set; } = 0.92;

        // A Folder Portal mirrors a real folder's live contents instead of holding
        // its own shortcuts. FolderPath then points at the mirrored folder.
        public bool IsPortal { get; set; } = false;

        public FenceSort Sort { get; set; } = FenceSort.Name;
        public FenceIconSize IconSize { get; set; } = FenceIconSize.Medium;

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
