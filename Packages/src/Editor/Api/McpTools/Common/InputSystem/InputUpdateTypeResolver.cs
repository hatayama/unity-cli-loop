#nullable enable
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.UnityCliLoop
{
    // Projects can process Input System events in dynamic, fixed, or manual mode.
    // Input simulation must follow that configured loop to avoid frame mismatches.
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
