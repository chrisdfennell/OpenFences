using System.Windows.Media;

namespace OpenFences
{
    public class FenceItem
    {
        public string Path { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public ImageSource? Icon { get; set; }
    }
}