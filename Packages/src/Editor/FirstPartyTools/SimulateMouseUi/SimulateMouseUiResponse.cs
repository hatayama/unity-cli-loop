#nullable enable

using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Simulate Mouse UI tool.
    /// </summary>
    public class SimulateMouseUiResponse : UnityCliLoopToolResponse
    {
        public bool Success { get; set; }
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? HitGameObjectName { get; set; }
        public float PositionX { get; set; }
        public float PositionY { get; set; }
        public float? EndPositionX { get; set; }
        public float? EndPositionY { get; set; }
        public bool InterruptedByPausePoint { get; set; }
        public string? PausePointId { get; set; }
        public int? PausePointHitCount { get; set; }
        public List<UnityCliLoopPausePointHit>? PausePointHits { get; set; }

        public SimulateMouseUiResponse()
        {
        }
    }
}
