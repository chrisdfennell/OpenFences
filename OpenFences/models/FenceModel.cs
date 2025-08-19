namespace OpenFences
{
    public class FenceModel
    {
        public string Name { get; set; } = "Fence";
        public string FolderPath { get; set; } = "";
        public double Left { get; set; }
        public double Top { get; set; }
        public double Width { get; set; } = 400;
        public double Height { get; set; } = 240;
        public bool Collapsed { get; set; } = false;
    }
}