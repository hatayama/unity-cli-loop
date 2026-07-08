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
                    return await ExecuteClick(parameters, eventSystem, ct).ConfigureAwait(false);

                case MouseAction.Drag:
                    return await ExecuteDragOneShot(parameters, eventSystem, ct).ConfigureAwait(false);

                case MouseAction.DragStart:
                    return await ExecuteDragStart(parameters, eventSystem, ct).ConfigureAwait(false);

                case MouseAction.DragMove:
                    return await ExecuteDragMove(parameters, ct).ConfigureAwait(false);

                case MouseAction.DragEnd:
                    return await ExecuteDragEnd(parameters, ct).ConfigureAwait(false);

                case MouseAction.LongPress:
                    return await ExecuteLongPress(parameters, eventSystem, ct).ConfigureAwait(false);

                default:
                    // Unreachable when TryFromSchema succeeds; kept as a defensive Success=false response
                    // instead of a throw so any future MouseAction addition surfaces as a validation failure.
                    return MouseUiSimulationResponseFactory.CreateFailure(
                        parameters,
                        $"Unknown mouse action: {parameters.Action}");
            }
        }

        // Input coordinates use top-left origin; Unity Screen space uses bottom-left origin.
        // Handles.GetMainGameViewSize() returns the Game view's target resolution (e.g. 1920x1080),
        // which matches the Canvas layout space — unlike Screen.height which returns the window pixel size.
        private static Vector2 InputToScreen(Vector2 inputPos)
        {
            float targetHeight = Handles.GetMainGameViewSize().y;
            return new Vector2(inputPos.x, targetHeight - inputPos.y);
        }

        private static Vector2 ScreenToInput(Vector2 screenPos)
        {
            float targetHeight = Handles.GetMainGameViewSize().y;
            return new Vector2(screenPos.x, targetHeight - screenPos.y);
        }

        private async Task<SimulateMouseUiResponse> ExecuteClick(
            MouseUiSimulationCommand parameters, EventSystem eventSystem, CancellationToken ct)
        {
            Vector2 inputPos = new(parameters.X, parameters.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            PointerEventData pointerData = MouseUiPointerTargetResolver.CreatePointerPressData(eventSystem, screenPos, parameters.Button);
            ResolvedPointerTargets resolvedTargets =
                MouseUiPointerTargetResolver.ResolvePressablePointerTargets(parameters, eventSystem, inputPos, screenPos, pointerData, MouseAction.Click);
            if (resolvedTargets.FailureResponse != null)
            {
                return resolvedTargets.FailureResponse;
            }

            if (parameters.BypassRaycast && resolvedTargets.Target == null)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"TargetPath '{parameters.TargetPath}' has no pointer click or pointer down handler.",
                    Action = MouseAction.Click.ToString(),
                    PositionX = inputPos.x,
                    PositionY = inputPos.y
                };
            }

            string? targetName = resolvedTargets.Target?.name;
            bool hitTarget = resolvedTargets.Target != null;
            SimulateMouseUiOverlayState.Update(
                MouseAction.Click, inputPos, null,
                targetName, Handles.GetMainGameViewSize());

            bool expandCompleted = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
            if (!expandCompleted)
            {
                _mainThreadCleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Click, inputPos, null, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            // Fire click events after expand animation so the user sees where the click lands
            ExecutePointerClickEvents(resolvedTargets, pointerData);

            bool dissipateCompleted = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!dissipateCompleted)
            {
                _mainThreadCleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateClickResult(parameters, inputPos, targetName, hitTarget);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return MouseUiSimulationResponseFactory.CreateClickResult(parameters, inputPos, targetName, hitTarget);
        }

        private async Task<SimulateMouseUiResponse> ExecuteLongPress(
            MouseUiSimulationCommand parameters, EventSystem eventSystem, CancellationToken ct)
        {
            if (parameters.Duration <= 0f || float.IsNaN(parameters.Duration) || float.IsInfinity(parameters.Duration))
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"Duration must be positive, got: {parameters.Duration}",
                    Action = MouseAction.LongPress.ToString()
                };
            }

            Vector2 inputPos = new(parameters.X, parameters.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            PointerEventData pointerData = MouseUiPointerTargetResolver.CreatePointerPressData(eventSystem, screenPos, parameters.Button);
            ResolvedPointerTargets resolvedTargets =
                MouseUiPointerTargetResolver.ResolvePressablePointerTargets(parameters, eventSystem, inputPos, screenPos, pointerData, MouseAction.LongPress);
            if (resolvedTargets.FailureResponse != null)
            {
                return resolvedTargets.FailureResponse;
            }

            if (parameters.BypassRaycast && resolvedTargets.Target == null)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"TargetPath '{parameters.TargetPath}' has no pointer down or pointer click handler.",
                    Action = MouseAction.LongPress.ToString(),
                    PositionX = inputPos.x,
                    PositionY = inputPos.y
                };
            }

            string? targetName = resolvedTargets.Target?.name;
            bool hitTarget = resolvedTargets.Target != null;
            bool shouldReleasePointer = resolvedTargets.RawTarget != null && resolvedTargets.Target != null;
            SimulateMouseUiOverlayState.Update(
                MouseAction.LongPress, inputPos, null,
                targetName, Handles.GetMainGameViewSize());

            bool expandCompleted = await MouseUiOverlayAnimator.PlayExpandAnimation(ct).ConfigureAwait(false);
            if (!expandCompleted)
            {
                _mainThreadCleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.LongPress, inputPos, null, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            ExecuteLongPressPointerDown(resolvedTargets, pointerData);

            try
            {
                // Hold for Duration seconds, updating elapsed time each frame for overlay display
                float startTime = Time.realtimeSinceStartup;
                float elapsed = 0f;
                while (elapsed < parameters.Duration)
                {
                    SimulateMouseUiOverlayState.UpdateLongPressElapsed(elapsed);
                    bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                    if (!frameReady)
                    {
                        _mainThreadCleanupScheduler.QueueOverlayClear();
                        return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.LongPress, inputPos, null, targetName);
                    }
                    await MainThreadSwitcher.SwitchToMainThread(ct);
                    elapsed = Time.realtimeSinceStartup - startTime;
                }
                SimulateMouseUiOverlayState.UpdateLongPressElapsed(parameters.Duration);
            }
            finally
            {
                // Ensure pointerUp fires even if the hold loop is cancelled
                if (shouldReleasePointer)
                {
                    _mainThreadCleanupScheduler.ExecuteCleanupOnMainThread(
                        () => ExecuteEvents.Execute(resolvedTargets.Target!, pointerData, ExecuteEvents.pointerUpHandler));
                }
            }

            bool dissipateCompleted = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!dissipateCompleted)
            {
                _mainThreadCleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateLongPressResult(parameters, inputPos, targetName, hitTarget);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return MouseUiSimulationResponseFactory.CreateLongPressResult(parameters, inputPos, targetName, hitTarget);
        }

        private static void ExecutePointerClickEvents(
            ResolvedPointerTargets resolvedTargets,
            PointerEventData pointerData)
        {
            if (resolvedTargets.RawTarget == null)
            {
                return;
            }

            if (resolvedTargets.PressTarget != null)
            {
                ExecuteEvents.ExecuteHierarchy(
                    resolvedTargets.RawTarget,
                    pointerData,
                    ExecuteEvents.pointerDownHandler);
            }

            if (resolvedTargets.Target != null)
            {
                ExecuteEvents.Execute(
                    resolvedTargets.Target,
                    pointerData,
                    ExecuteEvents.pointerUpHandler);
            }

            if (resolvedTargets.ClickTarget != null)
            {
                ExecuteEvents.Execute(
                    resolvedTargets.ClickTarget,
                    pointerData,
                    ExecuteEvents.pointerClickHandler);
            }
        }

        private static void ExecuteLongPressPointerDown(
            ResolvedPointerTargets resolvedTargets,
            PointerEventData pointerData)
        {
            if (resolvedTargets.RawTarget == null || resolvedTargets.Target == null)
            {
                return;
            }

            ExecuteEvents.ExecuteHierarchy(
                resolvedTargets.RawTarget,
                pointerData,
                ExecuteEvents.pointerDownHandler);
        }

        private PointerEventData InitiateDrag(
            EventSystem eventSystem,
            Vector2 screenPos,
            RaycastResult raycastResult,
            GameObject dragTarget,
            PointerEventData.InputButton inputButton)
        {
            PointerEventData pointerData = new(eventSystem)
            {
                position = screenPos,
                pressPosition = screenPos,
                button = inputButton,
                pointerCurrentRaycast = raycastResult,
                pointerPressRaycast = raycastResult,
                pointerDrag = dragTarget,
                rawPointerPress = raycastResult.gameObject
            };

            // Slider.OnPointerDown initializes m_Offset for handle positioning
            GameObject? pressTarget = ExecuteEvents.ExecuteHierarchy(
                raycastResult.gameObject, pointerData, ExecuteEvents.pointerDownHandler);
            pointerData.pointerPress = pressTarget;

            // ScrollRect.OnInitializePotentialDrag clears inertia, Slider sets useDragThreshold=false
            ExecuteEvents.Execute(dragTarget, pointerData, ExecuteEvents.initializePotentialDrag);

            return pointerData;
        }

        private async Task<SimulateMouseUiResponse> ExecuteDragOneShot(
            MouseUiSimulationCommand parameters, EventSystem eventSystem, CancellationToken ct)
        {
            Vector2 inputStart = new(parameters.FromX, parameters.FromY);
            Vector2 inputEnd = new(parameters.X, parameters.Y);
            Vector2 screenStart = InputToScreen(inputStart);
            Vector2 screenEnd = InputToScreen(inputEnd);
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
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, null);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool dissipateCompleted = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
                if (!dissipateCompleted)
                {
                    _mainThreadCleanupScheduler.QueueOverlayClear();
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
            PointerEventData pointerData = InitiateDrag(eventSystem, screenStart, startRaycast, target, PointerEventData.InputButton.Left);
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
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool dragCompleted = await InterpolateDragPosition(pointerData, target, screenEnd, parameters.DragSpeed, ct)
                    .ConfigureAwait(false);
                if (!dragCompleted)
                {
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    _mainThreadCleanupScheduler.QueueOverlayClear();
                    return MouseUiSimulationResponseFactory.CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
            }
            finally
            {
                _mainThreadCleanupScheduler.ExecuteCleanupOnMainThread(() => FinalizeDrag(pointerData, target, explicitDropTarget));
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.Drag, inputEnd, inputStart, targetName, Handles.GetMainGameViewSize());

            bool completedDissipate = await MouseUiOverlayAnimator.PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!completedDissipate)
            {
                _mainThreadCleanupScheduler.QueueOverlayClear();
                return MouseUiSimulationResponseFactory.CreateDragResult(parameters, inputStart, inputEnd, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return MouseUiSimulationResponseFactory.CreateDragResult(parameters, inputStart, inputEnd, targetName);
        }

        // Lifecycle must match StandaloneInputModule: raycast → pointerUp → drop → endDrag
        private void FinalizeDrag(PointerEventData pointerData, GameObject target, GameObject? explicitDropTarget)
        {
            if (explicitDropTarget != null)
            {
                pointerData.pointerCurrentRaycast = MouseUiPointerTargetResolver.CreateDirectRaycastResult(explicitDropTarget);
            }
            else
            {
                UpdatePointerRaycast(pointerData);
            }

            if (pointerData.pointerPress != null)
            {
                ExecuteEvents.Execute(pointerData.pointerPress, pointerData, ExecuteEvents.pointerUpHandler);
            }

            // Standard IDropHandler dispatch so Unity drop targets respond without manual workarounds
            GameObject? dropTarget = pointerData.pointerCurrentRaycast.gameObject;
            if (dropTarget != null)
            {
                ExecuteEvents.ExecuteHierarchy(dropTarget, pointerData, ExecuteEvents.dropHandler);
            }

            pointerData.dragging = false;
            ExecuteEvents.Execute(target, pointerData, ExecuteEvents.endDragHandler);
        }

        private void UpdatePointerRaycast(PointerEventData pointerData)
        {
            RaycastResult? hit = UiRaycastHelper.RaycastUI(pointerData.position, EventSystem.current);
            pointerData.pointerCurrentRaycast = hit ?? new RaycastResult();
        }

        private async Task<bool> InterpolateDragPosition(
            PointerEventData pointerData,
            GameObject target,
            Vector2 endPos,
            float dragSpeed,
            CancellationToken ct)
        {
            Debug.Assert(dragSpeed >= 0f, "dragSpeed must be non-negative");

            Vector2 startPos = pointerData.position;
            float distance = Vector2.Distance(startPos, endPos);
            float duration = dragSpeed > 0f ? distance / dragSpeed : 0f;

            if (duration <= 0f)
            {
                bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    return false;
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                Vector2 previousPosition = pointerData.position;
                pointerData.position = endPos;
                pointerData.delta = endPos - previousPosition;
                ExecuteEvents.Execute(target, pointerData, ExecuteEvents.dragHandler);

                SimulateMouseUiOverlayState.UpdatePosition(ScreenToInput(endPos));
                return true;
            }

            float startTime = Time.realtimeSinceStartup;
            float t;

            do
            {
                bool frameReady = await MouseUiEditorFrameWaiter.WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    return false;
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                float elapsed = Time.realtimeSinceStartup - startTime;
                t = Mathf.Clamp01(elapsed / duration);
                Vector2 previousPosition = pointerData.position;
                Vector2 currentPosition = Vector2.Lerp(startPos, endPos, t);

                pointerData.position = currentPosition;
                pointerData.delta = currentPosition - previousPosition;

                ExecuteEvents.Execute(target, pointerData, ExecuteEvents.dragHandler);

                SimulateMouseUiOverlayState.UpdatePosition(ScreenToInput(currentPosition));
            }
            while (t < 1.0f);

            return true;
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
            Vector2 screenPos = InputToScreen(inputPos);
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

            PointerEventData pointerData = InitiateDrag(eventSystem, screenPos, startRaycast, target, PointerEventData.InputButton.Left);
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
                        FinalizeDrag(pointerData, target, null);
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
            Vector2 screenEnd = InputToScreen(inputEnd);
            PointerEventData pointerData = MouseDragState.PointerData!;
            GameObject target = MouseDragState.Target!;
            string targetName = target.name;

            SimulateMouseUiOverlayState.Update(
                MouseAction.DragMove,
                ScreenToInput(pointerData.position),
                SimulateMouseUiOverlayState.DragStartPosition,
                targetName, Handles.GetMainGameViewSize());

            // Cancellation leaves drag state intact so the user can continue with DragMove/DragEnd
            bool dragCompleted = await InterpolateDragPosition(
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
            Vector2 screenEnd = InputToScreen(inputEnd);
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
                ScreenToInput(pointerData.position),
                SimulateMouseUiOverlayState.DragStartPosition,
                targetName, Handles.GetMainGameViewSize());

            try
            {
                bool dragCompleted = await InterpolateDragPosition(
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
                    FinalizeDrag(pointerData, target, explicitDropTarget);
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
