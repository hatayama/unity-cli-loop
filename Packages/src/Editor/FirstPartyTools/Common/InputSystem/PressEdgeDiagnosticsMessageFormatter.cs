#nullable enable

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Pure formatting of the diagnostic suffix appended to Press/KeyDown responses when the
    /// gameplay press edge (wasPressedThisFrame) was not observed. Why: the root cause of an
    /// unobserved edge cannot be reproduced on demand (see Round-6 investigation), so instead of
    /// a speculative behavior change this records enough context to diagnose the next real
    /// occurrence from the response alone.
    /// </summary>
    internal static class PressEdgeDiagnosticsMessageFormatter
    {
        public static string BuildSuffix(
            string? consumedByUpdateType,
            bool anyGameplayUpdateObserved,
            bool keyAlreadyPressedBeforeQueue)
        {
            if (keyAlreadyPressedBeforeQueue)
            {
                return " (key was already down before this action, so no press transition occurred)";
            }

            if (consumedByUpdateType == null)
            {
                return anyGameplayUpdateObserved
                    ? " (the key-down event was not consumed by any recorded Input System update, even though gameplay input updates ran during the wait)"
                    : " (no gameplay input update (Dynamic, Fixed, or Manual) ran during the wait, so gameplay polling never had a chance to observe it)";
            }

            if (consumedByUpdateType == "Editor")
            {
                return " (the key-down event was consumed during an Editor update, which gameplay Update polling cannot see)";
            }

            return $" (the key-down event was consumed during a {consumedByUpdateType} update, but wasPressedThisFrame was still not observed)";
        }
    }
}
