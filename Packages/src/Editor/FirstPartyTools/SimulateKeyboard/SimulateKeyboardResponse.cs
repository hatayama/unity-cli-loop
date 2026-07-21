#nullable enable

using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the response data returned by the Simulate Keyboard tool.
    /// </summary>
    public class SimulateKeyboardResponse : UnityCliLoopToolResponse
    {
        public string Message { get; set; } = "";
        public string Action { get; set; } = "";
        public string? KeyName { get; set; }
        public bool InterruptedByPausePoint { get; set; }
        public string? PausePointId { get; set; }
        public int? PausePointHitCount { get; set; }
        public List<UnityCliLoopPausePointHit>? PausePointHits { get; set; }
        public bool? PressEdgeObserved { get; set; }

        /// <summary>
        /// Extra observation frames spent holding the key after the normal duration window
        /// while waiting for wasPressedThisFrame. Null when release was not delayed.
        /// </summary>
        public int? PressHoldExtendedFrames { get; set; }

        /// <summary>
        /// Diagnostics set only when PressEdgeObserved is false, to help diagnose the next
        /// occurrence without changing Press/KeyDown timing, grace, or retry behavior.
        /// </summary>
        public string? PressEdgeConsumedByUpdateType { get; set; }

        public bool? PressEdgeAnyDynamicUpdateObserved { get; set; }

        public bool? PressEdgeKeyAlreadyPressedBeforeQueue { get; set; }

        public SimulateKeyboardResponse()
        {
        }
    }
}
