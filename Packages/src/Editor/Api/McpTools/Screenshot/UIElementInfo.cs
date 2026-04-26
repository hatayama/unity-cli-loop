namespace io.github.hatayama.uLoopMCP
{
    public class UIElementInfo
    {
        public string Name { get; set; } = "";
        public string Path { get; set; } = "";
        public string Type { get; set; } = "";
        public string Interaction { get; set; } = "";
        public float SimX { get; set; }
        public float SimY { get; set; }
        public float BoundsMinX { get; set; }
        public float BoundsMinY { get; set; }
        public float BoundsMaxX { get; set; }
        public float BoundsMaxY { get; set; }
        public string Label { get; set; } = "";
        public int SortingOrder { get; set; }
        public int SiblingIndex { get; set; }
    }
}
