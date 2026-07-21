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

        // The following three PressEdge* fields are diagnostics set only when PressEdgeObserved
        // is false, to help diagnose the next occurrence without changing Press/KeyDown timing,
        // grace, or retry behavior.

        /// <summary>
        /// Which Input System update type (if any) consumed the key-down event, or null if it
        /// was never consumed.
        /// </summary>
        public string? PressEdgeConsumedByUpdateType { get; set; }

        /// <summary>
        /// Whether any Dynamic update ran while waiting for the press edge to be observed.
        /// </summary>
        public bool? PressEdgeAnyDynamicUpdateObserved { get; set; }

        /// <summary>
        /// Whether the key was already pressed before this action was queued, meaning no press
        /// transition could have occurred.
        /// </summary>
        public bool? PressEdgeKeyAlreadyPressedBeforeQueue { get; set; }

        public SimulateKeyboardResponse()
        {
        }
    }
}
