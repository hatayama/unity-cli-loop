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
                if (!MouseUiPointerTargetResolver.TryResolveGameObjectPath(
                    parameters.TargetPath,
                    "TargetPath",
                    action,
                    inputPosition,
                    out rawTarget,
                    out SimulateMouseUiResponse? failureResponse))
                {
                    return (startRaycast, null, failureResponse);
                }

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
