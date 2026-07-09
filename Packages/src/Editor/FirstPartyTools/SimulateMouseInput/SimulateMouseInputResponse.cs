#nullable enable

using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Simulate Mouse Input tool.
    /// </summary>
    public class SimulateMouseInputResponse : UnityCliLoopToolResponse
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
        public bool InterruptedByPausePoint { get; set; }
        public string? PausePointId { get; set; }
        public int? PausePointHitCount { get; set; }
        public List<UnityCliLoopPausePointHit>? PausePointHits { get; set; }

        public SimulateMouseInputResponse()
        {
        }
    }
}
