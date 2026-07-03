#nullable enable

namespace io.github.hatayama.uLoopMCP
{
    public class SimulateMouseInputResponse : BaseToolResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? Button { get; set; }
        public float? PositionX { get; set; }
        public float? PositionY { get; set; }
        public string InputCoordinateSystem { get; set; } = "";
        public string UnityCoordinateSystem { get; set; } = "";
        public float? GameViewWidth { get; set; }
        public float? GameViewHeight { get; set; }
        public float? InputPositionX { get; set; }
        public float? InputPositionY { get; set; }
        public float? InjectedUnityPositionX { get; set; }
        public float? InjectedUnityPositionY { get; set; }
        public string CoordinateConversionFormula { get; set; } = "";

        public SimulateMouseInputResponse()
        {
        }
    }
}
