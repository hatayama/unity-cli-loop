#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Converts the runtime mouse-button value into the Input System control used by injected events.
    /// <summary>
    /// Resolves Mouse Button Control values from the available runtime context.
    /// </summary>
    internal static class MouseButtonControlResolver
    {
        public static ButtonControl GetButtonControl(Mouse mouse, MouseButton button)
        {
            Debug.Assert(mouse != null, "mouse must not be null.");

            switch (button)
            {
                case MouseButton.Right:
                    return mouse.rightButton;
                case MouseButton.Middle:
                    return mouse.middleButton;
                default:
                    Debug.Assert(button == MouseButton.Left, $"Unexpected MouseButton value: {button}");
                    return mouse.leftButton;
            }
        }
    }
}
#endif
