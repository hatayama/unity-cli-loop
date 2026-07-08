#nullable enable
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries raw and hierarchy-resolved pointer targets or a resolution failure.
    /// </summary>
    internal readonly struct ResolvedPointerTargets
    {
        private ResolvedPointerTargets(
            GameObject? rawTarget,
            GameObject? pressTarget,
            GameObject? clickTarget,
            GameObject? target,
            SimulateMouseUiResponse? failureResponse)
        {
            RawTarget = rawTarget;
            PressTarget = pressTarget;
            ClickTarget = clickTarget;
            Target = target;
            FailureResponse = failureResponse;
        }

        public static ResolvedPointerTargets Empty { get; } =
            new(null, null, null, null, null);

        public GameObject? RawTarget { get; }
        public GameObject? PressTarget { get; }
        public GameObject? ClickTarget { get; }
        public GameObject? Target { get; }
        public SimulateMouseUiResponse? FailureResponse { get; }

        public static ResolvedPointerTargets Success(
            GameObject rawTarget,
            GameObject? pressTarget,
            GameObject? clickTarget,
            GameObject? target)
        {
            return new ResolvedPointerTargets(rawTarget, pressTarget, clickTarget, target, null);
        }

        public static ResolvedPointerTargets Failure(
            SimulateMouseUiResponse? failureResponse)
        {
            Debug.Assert(failureResponse != null, "Failure response must exist when target resolution fails.");
            return new ResolvedPointerTargets(null, null, null, null, failureResponse);
        }
    }
}
