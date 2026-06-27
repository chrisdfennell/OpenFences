namespace OpenFences
{
    public enum FenceSort { Name, Type, DateModified, Size, Manual }
    public enum FenceIconSize { Small, Medium, Large }

    public class FenceModel
    {
        public string Name { get; set; } = "Fence";
        public string FolderPath { get; set; } = "";
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
    }
}
