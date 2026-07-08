#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates EventSystem mouse simulation for the bundled simulate-mouse-ui tool.
    /// </summary>
    public class SimulateMouseUiUseCase
    {
        // Wire-visible fragment of the paused preflight message; tests pin the composed string.
        public const string PausedActionDescription = "simulating UI input";

        private readonly MouseUiMainThreadCleanupScheduler _mainThreadCleanupScheduler = new();

        public async Task<SimulateMouseUiResponse> ExecuteAsync(
            SimulateMouseUiSchema request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new System.ArgumentNullException(nameof(request));
            }

            ct.ThrowIfCancellationRequested();
            _mainThreadCleanupScheduler.CaptureMainThreadContext();

            (MouseUiSimulationCommand? parameters, string? actionError) = MouseUiSimulationCommand.TryFromSchema(request);
            if (parameters == null)
            {
                // TryFromSchema guarantees actionError is non-null when command is null.
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = actionError!,
                    Action = request.Action.ToString()
                };
            }

            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            EventSystem? eventSystem = EventSystem.current;
            SimulateMouseUiResponse? validationFailure = MouseUiSimulationValidator.ValidateSimulationStart(
                parameters,
                eventSystem,
                PausedActionDescription);
            if (validationFailure != null)
            {
                return validationFailure;
            }
            Debug.Assert(eventSystem != null, "ValidateSimulationStart must reject a missing EventSystem.");
            EventSystem activeEventSystem = eventSystem!;

            MouseUiSimulationActivityLogger.LogSimulationStart(parameters, correlationId);
            OverlayCanvasFactory.EnsureExists();

            SimulateMouseUiResponse? dragStateFailure =
                MouseUiSimulationValidator.ValidateActiveDragState(parameters);
            if (dragStateFailure != null)
            {
                return dragStateFailure;
            }

            SimulateMouseUiResponse response =
                await ExecuteMouseAction(parameters, activeEventSystem, ct).ConfigureAwait(false);
            MouseUiSimulationActivityLogger.LogSimulationComplete(parameters, response, correlationId);

            return response;
        }

        private async Task<SimulateMouseUiResponse> ExecuteMouseAction(
            MouseUiSimulationCommand parameters,
            EventSystem eventSystem,
            CancellationToken ct)
        {
            switch (parameters.Action)
            {
                case MouseAction.Click:
                    return await MouseUiPressActionExecutor.ExecuteClick(
                        parameters,
                        eventSystem,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                case MouseAction.Drag:
                    return await MouseUiOneShotDragExecutor.ExecuteDragOneShot(
                        parameters,
                        eventSystem,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                case MouseAction.DragStart:
                    return await ExecuteDragStart(parameters, eventSystem, ct).ConfigureAwait(false);

                case MouseAction.DragMove:
                    return await ExecuteDragMove(parameters, ct).ConfigureAwait(false);

                case MouseAction.DragEnd:
                    return await ExecuteDragEnd(parameters, ct).ConfigureAwait(false);

                case MouseAction.LongPress:
                    return await MouseUiPressActionExecutor.ExecuteLongPress(
                        parameters,
                        eventSystem,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                default:
                    // Unreachable when TryFromSchema succeeds; kept as a defensive Success=false response
                    // instead of a throw so any future MouseAction addition surfaces as a validation failure.
                    return MouseUiSimulationResponseFactory.CreateFailure(
                        parameters,
                        $"Unknown mouse action: {parameters.Action}");
            }
        }

        private async Task<SimulateMouseUiResponse> ExecuteDragStart(
            MouseUiSimulationCommand parameters, EventSystem eventSystem, CancellationToken ct)
        {
            if (MouseDragState.IsDragging)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "A drag is already in progress. Call DragEnd first.",
                    Action = MouseAction.DragStart.ToString(),
                    PositionX = parameters.X,
                    PositionY = parameters.Y
                };
            }

            Vector2 inputPos = new(parameters.X, parameters.Y);
            Vector2 screenPos = MouseUiCoordinateConverter.InputToScreen(inputPos);
            RaycastResult? hit = parameters.BypassRaycast ? null : UiRaycastHelper.RaycastUI(screenPos, eventSystem);
            RaycastResult startRaycast = new();
            GameObject? rawTarget = null;

            if (parameters.BypassRaycast)
            {
                if (!MouseUiPointerTargetResolver.TryResolveGameObjectPath(
                    parameters.TargetPath,
                    "TargetPath",
                    MouseAction.DragStart,
                    inputPos,
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

            GameObject? target = rawTarget != null
                ? ExecuteEvents.GetEventHandler<IDragHandler>(rawTarget)
                : null;

            if (target == null)
            {
                SimulateMouseUiOverlayState.Update(
                    MouseAction.DragStart, inputPos, null, null, Handles.GetMainGameViewSize());
                bool expandCompleted = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
                if (!expandCompleted)
                {
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.DragStart, inputPos, null, null);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool dissipateCompleted = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
                if (!dissipateCompleted)
                {
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.DragStart, inputPos, null, null);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = parameters.BypassRaycast
                        ? $"TargetPath '{parameters.TargetPath}' has no drag handler."
                        : $"No draggable UI element at ({inputPos.x:F1}, {inputPos.y:F1}). Use find-game-objects or screenshot to verify positions.",
                    Action = MouseAction.DragStart.ToString(),
                    PositionX = inputPos.x,
                    PositionY = inputPos.y
                };
            }

            PointerEventData pointerData = MouseUiDragEventExecutor.InitiateDrag(eventSystem, screenPos, startRaycast, target, PointerEventData.InputButton.Left);
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.beginDragHandler);
            pointerData.dragging = true;

            MouseDragState.Target = target;
            MouseDragState.PointerData = pointerData;

            string targetName = target.name;
            SimulateMouseUiOverlayState.Update(
                MouseAction.DragStart, inputPos, inputPos, targetName, Handles.GetMainGameViewSize());

            bool animationCompleted = false;
            try
            {
                animationCompleted = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
                if (!animationCompleted)
                {
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.DragStart, inputPos, null, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
                animationCompleted = true;
            }
            finally
            {
                // Cancellation during animation leaves beginDrag dispatched; clean up
                if (!animationCompleted)
                {
                    _mainThreadCleanupScheduler.ExecuteCleanupOnMainThread(() =>
                    {
                        MouseUiDragEventExecutor.FinalizeDrag(pointerData, target, null);
                        MouseDragState.Clear();
                    });
                }
            }

            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = $"Drag started on '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1})",
                Action = MouseAction.DragStart.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<SimulateMouseUiResponse> ExecuteDragMove(
            MouseUiSimulationCommand parameters, CancellationToken ct)
        {
            if (!MouseDragState.IsDragging)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "No drag in progress. Call DragStart first.",
                    Action = MouseAction.DragMove.ToString(),
                    PositionX = parameters.X,
                    PositionY = parameters.Y
                };
            }

            Debug.Assert(MouseDragState.Target != null, "Target must not be null when IsDragging is true");
            Debug.Assert(MouseDragState.PointerData != null, "PointerData must not be null when IsDragging is true");

            SimulateMouseUiResponse? invalidResponse = ValidateDragStillActive(parameters.Action);
            if (invalidResponse != null)
            {
                return invalidResponse;
            }

            Vector2 inputEnd = new(parameters.X, parameters.Y);
            Vector2 screenEnd = MouseUiCoordinateConverter.InputToScreen(inputEnd);
            PointerEventData pointerData = MouseDragState.PointerData!;
            GameObject target = MouseDragState.Target!;
            string targetName = target.name;

            SimulateMouseUiOverlayState.Update(
                MouseAction.DragMove,
                MouseUiCoordinateConverter.ScreenToInput(pointerData.position),
                SimulateMouseUiOverlayState.DragStartPosition,
                targetName, Handles.GetMainGameViewSize());

            // Cancellation leaves drag state intact so the user can continue with DragMove/DragEnd
            bool dragCompleted = await MouseUiDragEventExecutor.InterpolateDragPosition(
                pointerData, target, screenEnd,
                parameters.DragSpeed, ct).ConfigureAwait(false);
            if (!dragCompleted)
            {
                _mainThreadCleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.DragMove, inputEnd, null, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            SimulateMouseUiOverlayState.AddWaypoint(inputEnd);

            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = $"Drag moved on '{targetName}' to ({inputEnd.x:F1}, {inputEnd.y:F1}) at {parameters.DragSpeed:F0} px/s",
                Action = MouseAction.DragMove.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputEnd.x,
                PositionY = inputEnd.y
            };
        }

        private async Task<SimulateMouseUiResponse> ExecuteDragEnd(
            MouseUiSimulationCommand parameters, CancellationToken ct)
        {
            if (!MouseDragState.IsDragging)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "No drag in progress. Call DragStart first.",
                    Action = MouseAction.DragEnd.ToString(),
                    PositionX = parameters.X,
                    PositionY = parameters.Y
                };
            }

            Debug.Assert(MouseDragState.Target != null, "Target must not be null when IsDragging is true");
            Debug.Assert(MouseDragState.PointerData != null, "PointerData must not be null when IsDragging is true");

            SimulateMouseUiResponse? invalidResponse = ValidateDragStillActive(parameters.Action);
            if (invalidResponse != null)
            {
                return invalidResponse;
            }

            Vector2 inputEnd = new(parameters.X, parameters.Y);
            Vector2 screenEnd = MouseUiCoordinateConverter.InputToScreen(inputEnd);
            PointerEventData pointerData = MouseDragState.PointerData!;
            GameObject target = MouseDragState.Target!;
            string targetName = target.name;
            GameObject? explicitDropTarget = null;

            if (!MouseUiPointerTargetResolver.TryResolveDropTargetPath(
                parameters,
                MouseAction.DragEnd,
                inputEnd,
                out explicitDropTarget,
                out SimulateMouseUiResponse? dropFailureResponse))
            {
                return dropFailureResponse!;
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.DragEnd,
                MouseUiCoordinateConverter.ScreenToInput(pointerData.position),
                SimulateMouseUiOverlayState.DragStartPosition,
                targetName, Handles.GetMainGameViewSize());

            try
            {
                bool dragCompleted = await MouseUiDragEventExecutor.InterpolateDragPosition(
                    pointerData, target, screenEnd,
                    parameters.DragSpeed, ct).ConfigureAwait(false);
                if (!dragCompleted)
                {
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.DragEnd, inputEnd, null, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.DragEnd, inputEnd, null, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
            }
            finally
            {
                _mainThreadCleanupScheduler.ExecuteCleanupOnMainThread(() =>
                {
                    MouseUiDragEventExecutor.FinalizeDrag(pointerData, target, explicitDropTarget);
                    MouseDragState.Clear();
                });
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.DragEnd, inputEnd, null, targetName, Handles.GetMainGameViewSize());

            bool dissipateCompleted = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!dissipateCompleted)
            {
                _mainThreadCleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateDragEndResult(parameters, inputEnd, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return MouseUiSimulationResponseFactory.CreateDragEndResult(parameters, inputEnd, targetName);
        }

        // User input during a CLI drag can cause Unity's StandaloneInputModule to
        // release or reassign the drag, leaving MouseDragState stale.
        private SimulateMouseUiResponse? ValidateDragStillActive(MouseAction action)
        {
            if (!MouseDragState.Target!.activeInHierarchy)
            {
                MouseDragState.Clear();
                SimulateMouseUiOverlayState.Clear();
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "Drag target was destroyed or deactivated during drag.",
                    Action = action.ToString()
                };
            }

            if (!MouseDragState.PointerData!.dragging ||
                MouseDragState.PointerData.pointerDrag != MouseDragState.Target)
            {
                MouseDragState.Clear();
                SimulateMouseUiOverlayState.Clear();
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "Drag was interrupted by user input or system event.",
                    Action = action.ToString()
                };
            }

            return null;
        }

    }
}
