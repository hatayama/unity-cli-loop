#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes a complete mouse UI drag in one request.
    /// </summary>
    internal static class MouseUiOneShotDragExecutor
    {
        internal static async Task<SimulateMouseUiResponse> ExecuteDragOneShot(
            MouseUiSimulationCommand parameters,
            EventSystem eventSystem,
            MouseUiMainThreadCleanupScheduler cleanupScheduler,
            CancellationToken ct)
        {
            Vector2 inputStart = new(parameters.FromX, parameters.FromY);
            Vector2 inputEnd = new(parameters.X, parameters.Y);
            Vector2 screenStart = MouseUiCoordinateConverter.InputToScreen(inputStart);
            Vector2 screenEnd = MouseUiCoordinateConverter.InputToScreen(inputEnd);
            RaycastResult? hit = parameters.BypassRaycast ? null : UiRaycastHelper.RaycastUI(screenStart, eventSystem);
            RaycastResult startRaycast = new();
            GameObject? rawTarget = null;

            if (parameters.BypassRaycast)
            {
                if (!MouseUiPointerTargetResolver.TryResolveGameObjectPath(
                    parameters.TargetPath,
                    "TargetPath",
                    MouseAction.Drag,
                    inputStart,
                    out rawTarget,
                    out SimulateMouseUiResponse? failureResponse))
                {
                    return failureResponse!;
                }

                startRaycast = MouseUiPointerTargetResolver.CreateDirectRaycastResult(rawTarget!);
            }
            else if (hit != null)
            {
                rawTarget = hit.Value.gameObject;
                startRaycast = hit.Value;
            }

            GameObject? explicitDropTarget = null;
            if (!MouseUiPointerTargetResolver.TryResolveDropTargetPath(
                parameters,
                MouseAction.Drag,
                inputEnd,
                out explicitDropTarget,
                out SimulateMouseUiResponse? dropFailureResponse))
            {
                return dropFailureResponse!;
            }

            // Execute dispatches only to the exact target; resolve the actual drag handler up the hierarchy
            GameObject? target = rawTarget != null
                ? ExecuteEvents.GetEventHandler<IDragHandler>(rawTarget)
                : null;

            if (target == null)
            {
                SimulateMouseUiOverlayState.Update(
                    MouseAction.Drag, inputStart, null, null, Handles.GetMainGameViewSize());
                bool expandCompleted = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
                if (!expandCompleted)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, null);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool dissipateCompleted = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
                if (!dissipateCompleted)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, null);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = parameters.BypassRaycast
                        ? $"TargetPath '{parameters.TargetPath}' has no drag handler."
                        : $"No draggable UI element at ({inputStart.x:F1}, {inputStart.y:F1}). Use find-game-objects or screenshot to verify positions.",
                    Action = MouseAction.Drag.ToString(),
                    PositionX = inputStart.x,
                    PositionY = inputStart.y,
                    EndPositionX = inputEnd.x,
                    EndPositionY = inputEnd.y
                };
            }

            // uGUI drag controls (ScrollRect, Slider) only respond to left-button drags
            PointerEventData pointerData = MouseUiDragEventExecutor.InitiateDrag(eventSystem, screenStart, startRaycast, target, PointerEventData.InputButton.Left);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.beginDragHandler);
            pointerData.dragging = true;

            string targetName = target.name;
            SimulateMouseUiOverlayState.Update(
                MouseAction.Drag, inputStart, inputStart, targetName, Handles.GetMainGameViewSize());

            try
            {
                bool expandCompleted = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
                if (!expandCompleted)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool dragCompleted = await MouseUiDragEventExecutor.InterpolateDragPosition(pointerData, target, screenEnd, parameters.DragSpeed, ct)
                    .ConfigureAwait(false);
                if (!dragCompleted)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
            }
            finally
            {
                cleanupScheduler.ExecuteCleanupOnMainThread(() => MouseUiDragEventExecutor.FinalizeDrag(pointerData, target, explicitDropTarget));
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.Drag, inputEnd, inputStart, targetName, Handles.GetMainGameViewSize());

            bool completedDissipate = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!completedDissipate)
            {
                cleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateDragResult(parameters, inputStart, inputEnd, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return MouseUiSimulationResponseFactory.CreateDragResult(parameters, inputStart, inputEnd, targetName);
        }
    }
}
