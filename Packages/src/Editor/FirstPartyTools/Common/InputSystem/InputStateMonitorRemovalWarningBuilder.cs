#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the response warning that tells the caller an input callback removed a state monitor
    /// while uloop applied input, which the suppressed assertion would otherwise have reported.
    /// </summary>
    internal static class InputStateMonitorRemovalWarningBuilder
    {
        internal const string MonitorRemovalWarning =
            "An input callback removed an Input System state monitor while this input was being applied " +
            "(for example, a performed callback that disables its own action map). " +
            "uloop suppressed the internal \"Assertion failed\" log that Unity's Input System raises in the Editor for this case. " +
            "If other actions are bound to the same control, they may have missed this input; " +
            "switch action maps on the next frame instead of inside the callback if that matters.";

        /// <summary>
        /// Returns existingWarning with the monitor-removal warning appended when the suppression count grew.
        /// </summary>
        public static string Append(string existingWarning, int suppressedCountBefore, int suppressedCountAfter)
        {
            Debug.Assert(
                suppressedCountAfter >= suppressedCountBefore,
                "suppression count must be monotonic");

            if (suppressedCountAfter == suppressedCountBefore)
            {
                return existingWarning;
            }

            if (string.IsNullOrEmpty(existingWarning))
            {
                return MonitorRemovalWarning;
            }

            return existingWarning + " " + MonitorRemovalWarning;
        }
    }
}
#endif
