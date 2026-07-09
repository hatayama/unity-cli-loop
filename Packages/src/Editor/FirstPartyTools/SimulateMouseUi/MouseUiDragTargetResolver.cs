#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves the draggable target and starting raycast for mouse UI drag actions.
    /// </summary>
    internal static class MouseUiDragTargetResolver
    {
        internal static (
            RaycastResult StartRaycast,
            GameObject? Target,
            SimulateMouseUiResponse? FailureResponse) Resolve(
                MouseUiSimulationCommand parameters,
                EventSystem eventSystem,
                MouseAction action,
                Vector2 inputPosition,
                Vector2 screenPosition)
        {
            RaycastResult? hit = parameters.BypassRaycast
                ? null
                : UiRaycastHelper.RaycastUI(screenPosition, eventSystem);
            RaycastResult startRaycast = new();
            GameObject? rawTarget = null;

            if (parameters.BypassRaycast)
            {
                (GameObject? Target, SimulateMouseUiResponse? FailureResponse) resolution =
                    MouseUiPointerTargetResolver.ResolveGameObjectPath(
                        parameters.TargetPath,
                        "TargetPath",
                        action,
                        inputPosition);
                if (resolution.FailureResponse != null)
                {
                    return (startRaycast, null, resolution.FailureResponse);
                }

                rawTarget = resolution.Target;
                startRaycast = MouseUiPointerTargetResolver.CreateDirectRaycastResult(rawTarget!);
            }
            else if (hit != null)
            {
                rawTarget = hit.Value.gameObject;
                startRaycast = hit.Value;
            }

            // Execute dispatches only to the exact target; resolve the actual drag handler up the hierarchy.
            GameObject? target = rawTarget != null
                ? ExecuteEvents.GetEventHandler<IDragHandler>(rawTarget)
                : null;

            return (startRaycast, target, null);
        }
    }
}
