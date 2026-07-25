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
            (RaycastResult startRaycast, GameObject? target, SimulateMouseUiResponse? targetFailureResponse) =
                MouseUiDragTargetResolver.Resolve(
                    parameters,
                    eventSystem,
                    MouseAction.Drag,
                    inputStart,
                    screenStart);
            if (targetFailureResponse != null)
            {
                return targetFailureResponse;
            }

            (GameObject? explicitDropTarget, SimulateMouseUiResponse? dropFailureResponse) =
                MouseUiPointerTargetResolver.ResolveDropTargetPath(
                    parameters,
                    MouseAction.Drag,
                    inputEnd);
            if (dropFailureResponse != null)
            {
                return dropFailureResponse;
            }

            if (target == null)
            {
                SimulateMouseUiOverlayState.Update(
                    MouseAction.Drag, inputStart, null, Handles.GetMainGameViewSize());
                MouseUiFrameWaitOutcome noTargetExpandOutcome = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
                if (noTargetExpandOutcome == MouseUiFrameWaitOutcome.TimedOut)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, null);
                }
                if (noTargetExpandOutcome == MouseUiFrameWaitOutcome.Paused)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateInterruptedResult(
                        MouseAction.Drag, inputStart, null,
                        "Drag stopped because Unity paused during Pause Point inspection. No draggable target was found at the start position, so no drag was initiated.");
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                MouseUiFrameWaitOutcome noTargetDissipateOutcome = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
                if (noTargetDissipateOutcome == MouseUiFrameWaitOutcome.TimedOut)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, null);
                }
                if (noTargetDissipateOutcome == MouseUiFrameWaitOutcome.Paused)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateInterruptedResult(
                        MouseAction.Drag, inputStart, null,
                        "Drag stopped because Unity paused during Pause Point inspection. No draggable target was found at the start position, so no drag was initiated.");
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
                MouseAction.Drag, inputStart, inputStart, Handles.GetMainGameViewSize());

            // Any Paused exit inside this try still runs FinalizeDrag in the finally below
            // (pointerUp/drop/endDrag), so every branch reports the drag as finalized early
            // rather than merely "not yet dispatched" like Click/LongPress's pre-input pause.
            const string DragInterruptedDuringMotionMessage =
                "Drag was finalized early (pointerUp/drop/endDrag dispatched via cleanup) because Unity paused during Pause Point inspection before the drag motion finished. The drag may have stopped short of the target position.";

            try
            {
                MouseUiFrameWaitOutcome expandOutcome = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
                if (expandOutcome == MouseUiFrameWaitOutcome.TimedOut)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                if (expandOutcome == MouseUiFrameWaitOutcome.Paused)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateInterruptedResult(
                        MouseAction.Drag, inputStart, targetName, DragInterruptedDuringMotionMessage);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                MouseUiFrameWaitOutcome dragOutcome = await MouseUiDragEventExecutor.InterpolateDragPosition(pointerData, target, screenEnd, parameters.DragSpeed, ct)
                    .ConfigureAwait(false);
                if (dragOutcome == MouseUiFrameWaitOutcome.TimedOut)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                if (dragOutcome == MouseUiFrameWaitOutcome.Paused)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateInterruptedResult(
                        MouseAction.Drag, inputStart, targetName, DragInterruptedDuringMotionMessage);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                MouseUiFrameWaitOutcome settleOutcome = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (settleOutcome == MouseUiFrameWaitOutcome.TimedOut)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                if (settleOutcome == MouseUiFrameWaitOutcome.Paused)
                {
                    cleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateInterruptedResult(
                        MouseAction.Drag, inputStart, targetName, DragInterruptedDuringMotionMessage);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
            }
            finally
            {
                cleanupScheduler.ExecuteCleanupOnMainThread(() => MouseUiDragEventExecutor.FinalizeDrag(pointerData, target, explicitDropTarget));
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.Drag, inputEnd, inputStart, Handles.GetMainGameViewSize());

            MouseUiFrameWaitOutcome dissipateOutcome = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (dissipateOutcome == MouseUiFrameWaitOutcome.TimedOut)
            {
                cleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateDragResult(parameters, inputStart, inputEnd, targetName);
            }
            if (dissipateOutcome == MouseUiFrameWaitOutcome.Paused)
            {
                cleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateInterruptedResult(
                    MouseAction.Drag, inputStart, targetName,
                    "Drag was already completed (target position reached, pointerUp/drop/endDrag dispatched). Unity paused during Pause Point inspection while the overlay animation was still playing; only the animation was interrupted.");
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return MouseUiSimulationResponseFactory.CreateDragResult(parameters, inputStart, inputEnd, targetName);
        }
    }
}
