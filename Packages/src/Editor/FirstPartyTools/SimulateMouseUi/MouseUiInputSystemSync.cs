#nullable enable
using UnityEngine;
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps Mouse.current aligned with the pointer position driven by mouse UI simulation.
    /// </summary>
    internal static class MouseUiInputSystemSync
    {
        // UI handlers can read Mouse.current alongside PointerEventData, so both paths must observe one position.
        internal static void SyncMousePosition(Vector2 screenPos)
        {
#if ULOOP_HAS_INPUT_SYSTEM
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            InputSystem.QueueDeltaStateEvent(mouse.position, screenPos);
            InputSystemUpdateHelper.RunExplicitUpdate(InputUpdateTypeResolver.Resolve());
#endif
        }
    }
}
