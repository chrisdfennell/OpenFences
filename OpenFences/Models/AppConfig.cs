using System.Collections.Generic;

namespace OpenFences
{
    public class AppOptions
    {
        public bool RunAtStartup { get; set; } = false;
        public bool HideIconsOnStartup { get; set; } = false;
        public bool DoubleClickDesktopToToggleIcons { get; set; } = true;
    }

    public class AppConfig
    {
        public List<FenceModel> Fences { get; set; } = new();
        public AppOptions Options { get; set; } = new();
    }
}
