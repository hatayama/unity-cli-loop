#nullable enable

namespace io.github.hatayama.uLoopMCP
{
    public class RaycastGridPointInfo
    {
        public string Label { get; set; } = "";
        public bool Hit { get; set; }
        public float InputX { get; set; }
        public float InputY { get; set; }
        public float InjectedUnityPositionX { get; set; }
        public float InjectedUnityPositionY { get; set; }
        public string? HitGameObjectName { get; set; }
        public string? HitGameObjectPath { get; set; }
        public string? HitLayer { get; set; }
        public int? HitLayerIndex { get; set; }
        public float? Distance { get; set; }
    }
}
