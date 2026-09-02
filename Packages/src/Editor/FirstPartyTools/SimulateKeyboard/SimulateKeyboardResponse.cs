#nullable enable

using System.Collections.Generic;
using Newtonsoft.Json;

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
        public string Warning { get; set; } = "";
        // Why per-property Ignore: null optional diagnostics must not serialize as JSON null;
        // agents treat missing vs null inconsistently. Do not change global serializer settings
        // or every tool's wire contract changes. Matches Go omitempty.
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? KeyName { get; set; }
        public bool InterruptedByPausePoint { get; set; }
        /// <summary>
        /// Id of the pause point that refused this call before it ran anything, null otherwise.
        /// Distinct from PausePointId, which reports a marker hit *during* the call: a refusal means
        /// no input was injected at all, so a caller reading only Success would miss that the action
        /// never happened. The CLI's --trigger diagnosis compares this against the marker it awaits.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? RejectedByActivePausePointId { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? PausePointId { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? PausePointHitCount { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<UnityCliLoopPausePointHit>? PausePointHits { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? PressEdgeObserved { get; set; }

        /// <summary>
        /// Extra observation frames spent holding the key after the normal duration window
        /// while waiting for wasPressedThisFrame. Null when release was not delayed.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public int? PressHoldExtendedFrames { get; set; }

        // The following three PressEdge* fields are diagnostics set only when PressEdgeObserved
        // is false, to help diagnose the next occurrence without changing Press/KeyDown timing,
        // grace, or retry behavior.

        /// <summary>
        /// Which Input System update type (if any) consumed the key-down event, or null if it
        /// was never consumed.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string? PressEdgeConsumedByUpdateType { get; set; }

        /// <summary>
        /// Whether any gameplay input update (Dynamic, Fixed, or Manual) ran while waiting for
        /// the press edge to be observed. Editor updates do not count.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? PressEdgeAnyGameplayUpdateObserved { get; set; }

        /// <summary>
        /// Whether the key was already pressed before this action was queued, meaning no press
        /// transition could have occurred.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? PressEdgeKeyAlreadyPressedBeforeQueue { get; set; }

        // KeyStateTrackedHeld / KeyStateDeviceIsPressed are set on KeyDown "already held"
        // and KeyUp "not currently held" rejections, and on successful KeyUp, so callers can
        // tell whether the uloop-side tracker and the Input System device agree.

        /// <summary>
        /// Whether Unity CLI Loop's own key-hold tracker (not the Input System device) considered
        /// the key held at the moment of the diagnostic read.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? KeyStateTrackedHeld { get; set; }

        /// <summary>
        /// Whether the Input System device (<c>keyboard[key].isPressed</c>) reported the key as
        /// pressed at the moment of the diagnostic read.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? KeyStateDeviceIsPressed { get; set; }

        /// <summary>
        /// Key names released by the ReleaseAll action (bookkeeping and/or device). Null for other actions.
        /// </summary>
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public List<string>? ReleasedKeys { get; set; }

        /// <summary>
        /// Per-key device readback after ReleaseAll injection. Null for other actions.
        /// </summary>
        public List<ReleasedKeyState>? ReleasedKeyStates { get; set; }

        /// <summary>
        /// <c>InputState.currentUpdateType</c> at the ReleaseAll device readback. Empty when omitted.
        /// </summary>
        public string? KeyStateReadUpdateType { get; set; }

        /// <summary>
        /// True when a one-shot player-update latch sync was scheduled after this ReleaseAll or KeyUp.
        /// Omitted when false.
        /// </summary>
        public bool DeferredLatchSyncScheduled { get; set; }

        public bool ShouldSerializeReleasedKeyStates()
        {
            return ReleasedKeyStates != null;
        }

        public bool ShouldSerializeKeyStateReadUpdateType()
        {
            return !string.IsNullOrEmpty(KeyStateReadUpdateType);
        }

        public bool ShouldSerializeDeferredLatchSyncScheduled()
        {
            return DeferredLatchSyncScheduled;
        }

        public bool ShouldSerializeWarning()
        {
            return !string.IsNullOrEmpty(Warning);
        }

        public SimulateKeyboardResponse()
        {
        }
    }
}
