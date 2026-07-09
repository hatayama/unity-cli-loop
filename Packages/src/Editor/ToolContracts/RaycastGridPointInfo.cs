#nullable enable

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Describes one coarse raycast grid sample point used to preview 3D physics hits on a screenshot.
    /// </summary>
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
