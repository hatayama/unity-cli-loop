namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Summarizes raycast grid hits for one physics layer.
    /// </summary>
    public class RaycastLayerSummaryInfo
    {
        public string Layer { get; set; } = "";
        public int LayerIndex { get; set; }
        public int HitCount { get; set; }
        public string RepresentativeObjectPath { get; set; } = "";
    }
}
