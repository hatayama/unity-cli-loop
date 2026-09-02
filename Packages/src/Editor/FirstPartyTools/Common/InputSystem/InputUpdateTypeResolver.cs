#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Projects can process Input System events in dynamic, fixed, or manual mode.
    // Input simulation must follow that configured loop to avoid frame mismatches.
    /// <summary>
    /// Resolves Input Update Type values from the available runtime context.
    /// </summary>
    internal static class InputUpdateTypeResolver
    {
        public static InputUpdateType Resolve()
        {
            InputSettings? settings = InputSystem.settings;
            if (settings == null)
            {
                return InputUpdateType.Dynamic;
            }

            InputSettings.UpdateMode updateMode = settings.updateMode;
            switch (updateMode)
            {
                case InputSettings.UpdateMode.ProcessEventsInFixedUpdate:
                    // Paused screens commonly set timeScale to 0, which stops fixed ticks entirely.
                    // Falling back to Dynamic keeps input simulation responsive for those menus.
                    return IsPausedFixedUpdate(settings) ? InputUpdateType.Dynamic : InputUpdateType.Fixed;

                case InputSettings.UpdateMode.ProcessEventsManually:
                    return InputUpdateType.Manual;

                default:
                    return InputUpdateType.Dynamic;
            }
        }

        public static bool IsMatch(InputUpdateType current, InputUpdateType expected)
        {
            return (current & expected) == expected;
        }

        // A press edge is visible to gameplay polling only when the Input System processed it
        // in one of the player-loop update types; the Editor tick never surfaces
        // wasPressedThisFrame to gameplay Update, and None means no update ran at all. Press-edge
        // observation and its miss diagnostics must agree on this set, otherwise a Fixed or Manual
        // project reads "no gameplay update ran" while its gameplay updates did run.
        public static bool IsGameplayUpdate(InputUpdateType updateType)
        {
            return updateType == InputUpdateType.Dynamic
                || updateType == InputUpdateType.Fixed
                || updateType == InputUpdateType.Manual;
        }

        public static bool RequiresExplicitUpdate()
        {
            InputSettings? settings = InputSystem.settings;
            if (settings == null)
            {
                return false;
            }

            if (settings.updateMode == InputSettings.UpdateMode.ProcessEventsManually)
            {
                return true;
            }

            return IsPausedFixedUpdate(settings);
        }

        private static bool IsPausedFixedUpdate(InputSettings settings)
        {
            return settings.updateMode == InputSettings.UpdateMode.ProcessEventsInFixedUpdate && Time.timeScale <= 0f;
        }
    }
}
#endif
