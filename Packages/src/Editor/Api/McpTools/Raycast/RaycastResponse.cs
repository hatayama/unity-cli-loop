#nullable enable

namespace io.github.hatayama.uLoopMCP
{
    public class RaycastResponse : BaseToolResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public bool Hit { get; set; }
        public string? HitGameObjectName { get; set; }
        public string? HitGameObjectPath { get; set; }
        public int? HitLayer { get; set; }
        public string? HitLayerName { get; set; }
        public float? Distance { get; set; }
        public float? HitPointX { get; set; }
        public float? HitPointY { get; set; }
        public float? HitPointZ { get; set; }
        public float? HitNormalX { get; set; }
        public float? HitNormalY { get; set; }
        public float? HitNormalZ { get; set; }
        public string InputCoordinateSystem { get; set; } = "";
        public string UnityCoordinateSystem { get; set; } = "";
        public float GameViewWidth { get; set; }
        public float GameViewHeight { get; set; }
        public float InputPositionX { get; set; }
        public float InputPositionY { get; set; }
        public float InjectedUnityPositionX { get; set; }
        public float InjectedUnityPositionY { get; set; }
        public string CoordinateConversionFormula { get; set; } = "";

        public RaycastResponse()
        {
        }
    }
}
