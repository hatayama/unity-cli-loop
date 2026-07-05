#nullable enable
using System;
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

        private const float EXPAND_DURATION = SimulateMouseUiAnimationConstants.EXPAND_DURATION;
        private const float EXPAND_START_SCALE = SimulateMouseUiAnimationConstants.EXPAND_START_SCALE;
        private const float DISSIPATE_DURATION = SimulateMouseUiAnimationConstants.DISSIPATE_DURATION;
        private SynchronizationContext? _mainThreadContext;

        public async Task<SimulateMouseUiResponse> ExecuteAsync(
            SimulateMouseUiSchema request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new System.ArgumentNullException(nameof(request));
            }

            ct.ThrowIfCancellationRequested();
            CaptureMainThreadContext();
            MouseUiSimulationCommand parameters = MouseUiSimulationCommand.FromSchema(request);

            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            EventSystem? eventSystem = EventSystem.current;
            SimulateMouseUiResponse? validationFailure = ValidateSimulationStart(parameters, eventSystem);
            if (validationFailure != null)
            {
                return validationFailure;
            }
            Debug.Assert(eventSystem != null, "ValidateSimulationStart must reject a missing EventSystem.");
            EventSystem activeEventSystem = eventSystem!;

            LogSimulationStart(parameters, correlationId);
            EnsureOverlayExists();

            SimulateMouseUiResponse? dragStateFailure = ValidateActiveDragState(parameters);
            if (dragStateFailure != null)
            {
                return dragStateFailure;
            }

            SimulateMouseUiResponse response =
                await ExecuteMouseAction(parameters, activeEventSystem, ct).ConfigureAwait(false);
            LogSimulationComplete(parameters, response, correlationId);

            return response;
        }

        private static SimulateMouseUiResponse? ValidateSimulationStart(
            MouseUiSimulationCommand parameters,
            EventSystem? eventSystem)
        {
            ValidationResult playModeResult = PlayModeToolPreflightService.RequireActiveAndNotPaused(PausedActionDescription);
            if (!playModeResult.IsValid)
            {
                return CreateFailure(parameters, playModeResult.ErrorMessage);
            }

            if (eventSystem == null)
            {
                return CreateFailure(
                    parameters,
                    "No EventSystem found in the scene. Ensure an EventSystem GameObject exists.");
            }

            return ValidateSimulationRequestOptions(parameters);
        }

        private static SimulateMouseUiResponse? ValidateSimulationRequestOptions(
            MouseUiSimulationCommand parameters)
        {
            if (parameters.Action != MouseAction.Click && parameters.Action != MouseAction.LongPress && parameters.DragSpeed < 0f)
            {
                return CreateFailure(parameters, $"DragSpeed must be non-negative, got: {parameters.DragSpeed}");
            }

            if (IsDragAction(parameters.Action) && parameters.Button != MouseButton.Left)
            {
                return CreateFailure(
                    parameters,
                    $"Drag actions only support Left button (uGUI ignores non-left drags), got: {parameters.Button}");
            }

            if (parameters.BypassRaycast && !SupportsBypassRaycast(parameters.Action))
            {
                return CreateFailure(parameters, "BypassRaycast is not supported for this action.");
            }

            if (parameters.BypassRaycast &&
                RequiresBypassTargetPath(parameters.Action) &&
                string.IsNullOrWhiteSpace(parameters.TargetPath))
            {
                return CreateFailure(
                    parameters,
                    "TargetPath is required when BypassRaycast is true for Click, LongPress, Drag, or DragStart.");
            }

            if (!string.IsNullOrWhiteSpace(parameters.DropTargetPath) &&
                parameters.Action != MouseAction.Drag &&
                parameters.Action != MouseAction.DragEnd)
            {
                return CreateFailure(parameters, "DropTargetPath supports Drag and DragEnd only.");
            }

            return null;
        }

        private static SimulateMouseUiResponse? ValidateActiveDragState(MouseUiSimulationCommand parameters)
        {
            if (!MouseDragState.IsDragging || !RequiresIdlePointer(parameters.Action))
            {
                return null;
            }

            return CreateFailure(
                parameters,
                $"Cannot {parameters.Action.ToString()} while a split drag is active. Call DragEnd first.");
        }

        private static bool RequiresIdlePointer(MouseAction action)
        {
            return action == MouseAction.Click || action == MouseAction.Drag || action == MouseAction.LongPress;
        }

        private static SimulateMouseUiResponse CreateFailure(
            MouseUiSimulationCommand parameters,
            string message)
        {
            return new SimulateMouseUiResponse
            {
                Success = false,
                Message = message,
                Action = parameters.Action.ToString()
            };
        }

        private static void LogSimulationStart(MouseUiSimulationCommand parameters, string correlationId)
        {
            VibeLogger.LogInfo(
                "simulate_mouse_start",
                "Mouse simulation started",
                new
                {
                    Action = parameters.Action.ToString(),
                    X = parameters.X,
                    Y = parameters.Y,
                    BypassRaycast = parameters.BypassRaycast,
                    TargetPath = parameters.TargetPath,
                    DropTargetPath = parameters.DropTargetPath
                },
                correlationId: correlationId
            );
        }

        private static void LogSimulationComplete(
            MouseUiSimulationCommand parameters,
            SimulateMouseUiResponse response,
            string correlationId)
        {
            VibeLogger.LogInfo(
                "simulate_mouse_complete",
                $"Mouse simulation completed: {response.Message}",
                new { Action = parameters.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );
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
                    throw new ArgumentException($"Unknown mouse action: {parameters.Action}");
            }
        }

        private static void EnsureOverlayExists()
        {
            OverlayCanvasFactory.EnsureExists();
        }

        private static PointerEventData.InputButton ToInputButton(MouseButton button)
        {
            switch (button)
            {
                case MouseButton.Right:
                    return PointerEventData.InputButton.Right;
                case MouseButton.Middle:
                    return PointerEventData.InputButton.Middle;
                default:
                    return PointerEventData.InputButton.Left;
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
            PointerEventData pointerData = CreatePointerPressData(eventSystem, screenPos, parameters.Button);
            ResolvedPointerTargets resolvedTargets =
                ResolvePressablePointerTargets(parameters, eventSystem, inputPos, screenPos, pointerData, MouseAction.Click);
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

            bool expandCompleted = await PlayExpandAnimation(ct).ConfigureAwait(false);
            if (!expandCompleted)
            {
                QueueOverlayClear();
                return CreateFrameTimeoutResult(MouseAction.Click, inputPos, null, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            // Fire click events after expand animation so the user sees where the click lands
            ExecutePointerClickEvents(resolvedTargets, pointerData);

            bool dissipateCompleted = await PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!dissipateCompleted)
            {
                QueueOverlayClear();
                return CreateClickResult(parameters, inputPos, targetName, hitTarget);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return CreateClickResult(parameters, inputPos, targetName, hitTarget);
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
            PointerEventData pointerData = CreatePointerPressData(eventSystem, screenPos, parameters.Button);
            ResolvedPointerTargets resolvedTargets =
                ResolvePressablePointerTargets(parameters, eventSystem, inputPos, screenPos, pointerData, MouseAction.LongPress);
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

            bool expandCompleted = await PlayExpandAnimation(ct).ConfigureAwait(false);
            if (!expandCompleted)
            {
                QueueOverlayClear();
                return CreateFrameTimeoutResult(MouseAction.LongPress, inputPos, null, targetName);
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
                    bool frameReady = await WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                    if (!frameReady)
                    {
                        QueueOverlayClear();
                        return CreateFrameTimeoutResult(MouseAction.LongPress, inputPos, null, targetName);
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
                    ExecuteCleanupOnMainThread(
                        () => ExecuteEvents.Execute(resolvedTargets.Target!, pointerData, ExecuteEvents.pointerUpHandler));
                }
            }

            bool dissipateCompleted = await PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!dissipateCompleted)
            {
                QueueOverlayClear();
                return CreateLongPressResult(parameters, inputPos, targetName, hitTarget);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return CreateLongPressResult(parameters, inputPos, targetName, hitTarget);
        }

        private static PointerEventData CreatePointerPressData(
            EventSystem eventSystem,
            Vector2 screenPos,
            MouseButton button)
        {
            return new PointerEventData(eventSystem)
            {
                position = screenPos,
                pressPosition = screenPos,
                button = ToInputButton(button)
            };
        }

        private static ResolvedPointerTargets ResolvePressablePointerTargets(
            MouseUiSimulationCommand parameters,
            EventSystem eventSystem,
            Vector2 inputPos,
            Vector2 screenPos,
            PointerEventData pointerData,
            MouseAction action)
        {
            if (parameters.BypassRaycast)
            {
                return ResolveBypassPressablePointerTargets(parameters, inputPos, pointerData, action);
            }

            RaycastResult? hit = RaycastUI(screenPos, eventSystem);
            if (hit == null)
            {
                return ResolvedPointerTargets.Empty;
            }

            return ResolveRaycastPressablePointerTargets(hit.Value, pointerData);
        }

        private static ResolvedPointerTargets ResolveBypassPressablePointerTargets(
            MouseUiSimulationCommand parameters,
            Vector2 inputPos,
            PointerEventData pointerData,
            MouseAction action)
        {
            if (!TryResolveGameObjectPath(
                parameters.TargetPath,
                "TargetPath",
                action,
                inputPos,
                out GameObject? rawTarget,
                out SimulateMouseUiResponse? failureResponse))
            {
                return ResolvedPointerTargets.Failure(failureResponse);
            }

            RaycastResult directRaycast = CreateDirectRaycastResult(rawTarget!);
            pointerData.pointerCurrentRaycast = directRaycast;
            pointerData.pointerPressRaycast = directRaycast;

            return CreateResolvedPressablePointerTargets(rawTarget!, pointerData);
        }

        private static ResolvedPointerTargets ResolveRaycastPressablePointerTargets(
            RaycastResult hit,
            PointerEventData pointerData)
        {
            GameObject rawTarget = hit.gameObject;
            pointerData.pointerCurrentRaycast = hit;
            pointerData.pointerPressRaycast = hit;

            return CreateResolvedPressablePointerTargets(rawTarget, pointerData);
        }

        private static ResolvedPointerTargets CreateResolvedPressablePointerTargets(
            GameObject rawTarget,
            PointerEventData pointerData)
        {
            // Execute dispatches only to the exact target; composite controls need hierarchy traversal.
            GameObject? pressTarget = ExecuteEvents.GetEventHandler<IPointerDownHandler>(rawTarget);
            GameObject? clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(rawTarget);
            GameObject? target = pressTarget ?? clickTarget;
            if (target != null)
            {
                pointerData.pointerPress = target;
                pointerData.rawPointerPress = rawTarget;
            }

            return ResolvedPointerTargets.Success(rawTarget, pressTarget, clickTarget, target);
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
            RaycastResult? hit = parameters.BypassRaycast ? null : RaycastUI(screenStart, eventSystem);
            RaycastResult startRaycast = new();
            GameObject? rawTarget = null;

            if (parameters.BypassRaycast)
            {
                if (!TryResolveGameObjectPath(
                    parameters.TargetPath,
                    "TargetPath",
                    MouseAction.Drag,
                    inputStart,
                    out rawTarget,
                    out SimulateMouseUiResponse? failureResponse))
                {
                    return failureResponse!;
                }

                startRaycast = CreateDirectRaycastResult(rawTarget!);
            }
            else if (hit != null)
            {
                rawTarget = hit.Value.gameObject;
                startRaycast = hit.Value;
            }

            GameObject? explicitDropTarget = null;
            if (!TryResolveDropTargetPath(
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
                bool expandCompleted = await PlayExpandAnimation(ct).ConfigureAwait(false);
                if (!expandCompleted)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, null);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool dissipateCompleted = await PlayDissipateAnimation(ct).ConfigureAwait(false);
                if (!dissipateCompleted)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, null);
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
                bool expandCompleted = await PlayExpandAnimation(ct).ConfigureAwait(false);
                if (!expandCompleted)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool dragCompleted = await InterpolateDragPosition(pointerData, target, screenEnd, parameters.DragSpeed, ct)
                    .ConfigureAwait(false);
                if (!dragCompleted)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool frameReady = await WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.Drag, inputStart, inputEnd, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
            }
            finally
            {
                ExecuteCleanupOnMainThread(() => FinalizeDrag(pointerData, target, explicitDropTarget));
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.Drag, inputEnd, inputStart, targetName, Handles.GetMainGameViewSize());

            bool completedDissipate = await PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!completedDissipate)
            {
                QueueOverlayClear();
                return CreateDragResult(parameters, inputStart, inputEnd, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return CreateDragResult(parameters, inputStart, inputEnd, targetName);
        }

        // Lifecycle must match StandaloneInputModule: raycast → pointerUp → drop → endDrag
        private void FinalizeDrag(PointerEventData pointerData, GameObject target, GameObject? explicitDropTarget)
        {
            if (explicitDropTarget != null)
            {
                pointerData.pointerCurrentRaycast = CreateDirectRaycastResult(explicitDropTarget);
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
                bool frameReady = await WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
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
                bool frameReady = await WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
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
            RaycastResult? hit = parameters.BypassRaycast ? null : RaycastUI(screenPos, eventSystem);
            RaycastResult startRaycast = new();
            GameObject? rawTarget = null;

            if (parameters.BypassRaycast)
            {
                if (!TryResolveGameObjectPath(
                    parameters.TargetPath,
                    "TargetPath",
                    MouseAction.DragStart,
                    inputPos,
                    out rawTarget,
                    out SimulateMouseUiResponse? failureResponse))
                {
                    return failureResponse!;
                }

                startRaycast = CreateDirectRaycastResult(rawTarget!);
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
                bool expandCompleted = await PlayExpandAnimation(ct).ConfigureAwait(false);
                if (!expandCompleted)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.DragStart, inputPos, null, null);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool dissipateCompleted = await PlayDissipateAnimation(ct).ConfigureAwait(false);
                if (!dissipateCompleted)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.DragStart, inputPos, null, null);
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
                animationCompleted = await PlayExpandAnimation(ct).ConfigureAwait(false);
                if (!animationCompleted)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.DragStart, inputPos, null, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
                animationCompleted = true;
            }
            finally
            {
                // Cancellation during animation leaves beginDrag dispatched; clean up
                if (!animationCompleted)
                {
                    ExecuteCleanupOnMainThread(() =>
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
                QueueOverlayClear();
                return CreateFrameTimeoutResult(MouseAction.DragMove, inputEnd, null, targetName);
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

            if (!TryResolveDropTargetPath(
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
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.DragEnd, inputEnd, null, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);

                bool frameReady = await WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    QueueOverlayClear();
                    return CreateFrameTimeoutResult(MouseAction.DragEnd, inputEnd, null, targetName);
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
            }
            finally
            {
                ExecuteCleanupOnMainThread(() =>
                {
                    FinalizeDrag(pointerData, target, explicitDropTarget);
                    MouseDragState.Clear();
                });
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.DragEnd, inputEnd, null, targetName, Handles.GetMainGameViewSize());

            bool dissipateCompleted = await PlayDissipateAnimation(ct).ConfigureAwait(false);
            if (!dissipateCompleted)
            {
                QueueOverlayClear();
                return CreateDragEndResult(parameters, inputEnd, targetName);
            }
            await MainThreadSwitcher.SwitchToMainThread(ct);

            return CreateDragEndResult(parameters, inputEnd, targetName);
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

        private static async Task<bool> PlayExpandAnimation(CancellationToken ct)
        {
            SimulateMouseUiOverlay overlay = OverlayCanvasFactory.VisualizationCanvas.MouseUiOverlay;

            // Previous dissipate sets alpha to 0; restore before expand starts
            overlay.SetAlpha(1f);

            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            while (elapsed < EXPAND_DURATION)
            {
                float t = elapsed / EXPAND_DURATION;
                overlay.SetCursorScale(Mathf.Lerp(EXPAND_START_SCALE, 1f, t));
                bool frameReady = await WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    return false;
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
                elapsed = Time.realtimeSinceStartup - startTime;
            }
            overlay.SetCursorScale(1f);
            return true;
        }

        private static async Task<bool> PlayDissipateAnimation(CancellationToken ct)
        {
            SimulateMouseUiOverlay overlay = OverlayCanvasFactory.VisualizationCanvas.MouseUiOverlay;

            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            while (elapsed < DISSIPATE_DURATION)
            {
                float t = elapsed / DISSIPATE_DURATION;
                overlay.SetCursorScale(Mathf.Lerp(1f, 0f, t));
                overlay.SetAlpha(Mathf.Lerp(1f, 0f, t));
                bool frameReady = await WaitForEditorFrameAndSwitchToMainThreadAsync(ct).ConfigureAwait(false);
                if (!frameReady)
                {
                    return false;
                }
                await MainThreadSwitcher.SwitchToMainThread(ct);
                elapsed = Time.realtimeSinceStartup - startTime;
            }
            overlay!.SetCursorScale(0f);
            overlay!.SetAlpha(0f);
            SimulateMouseUiOverlayState.Clear();
            return true;
        }

        private static async Task<bool> WaitForEditorFrameAndSwitchToMainThreadAsync(CancellationToken ct)
        {
            bool frameReady = await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                1,
                UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                ct).ConfigureAwait(false);
            if (!frameReady)
            {
                return false;
            }

            await MainThreadSwitcher.SwitchToMainThread(ct);
            return true;
        }

        private void CaptureMainThreadContext()
        {
            _mainThreadContext = SynchronizationContext.Current;
            Debug.Assert(_mainThreadContext != null, "Main thread synchronization context must be captured.");
        }

        private void QueueOverlayClear()
        {
            ExecuteCleanupOnMainThread(SimulateMouseUiOverlayState.Clear);
        }

        private void ExecuteCleanupOnMainThread(Action cleanup)
        {
            Debug.Assert(cleanup != null, "cleanup must not be null");
            if (cleanup == null)
            {
                throw new ArgumentNullException(nameof(cleanup));
            }

            if (MainThreadSwitcher.IsMainThread)
            {
                cleanup();
                return;
            }

            SynchronizationContext? context = _mainThreadContext;
            Debug.Assert(context != null, "Main thread synchronization context must be captured before cleanup.");
            if (context == null)
            {
                throw new InvalidOperationException("Main thread synchronization context was not captured.");
            }

            // Why: timeout continuations can run on timer threads while Unity objects must still be cleaned up on the Editor thread.
            context.Post(_ => cleanup(), null);
        }

        private static SimulateMouseUiResponse CreateFrameTimeoutResult(
            MouseAction action,
            Vector2 position,
            Vector2? endPosition,
            string? hitGameObjectName)
        {
            return new SimulateMouseUiResponse
            {
                Success = false,
                Message = $"Timed out after {UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS}ms while waiting for an editor frame.",
                Action = action.ToString(),
                HitGameObjectName = hitGameObjectName,
                PositionX = position.x,
                PositionY = position.y,
                EndPositionX = endPosition.HasValue ? endPosition.Value.x : null,
                EndPositionY = endPosition.HasValue ? endPosition.Value.y : null
            };
        }

        private static SimulateMouseUiResponse CreateClickResult(
            MouseUiSimulationCommand parameters,
            Vector2 inputPos,
            string? targetName,
            bool hitTarget)
        {
            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = hitTarget
                    ? parameters.BypassRaycast
                        ? $"Bypass-clicked '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1}) via '{parameters.TargetPath}'"
                        : $"Clicked '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1})"
                    : $"Clicked at ({inputPos.x:F1}, {inputPos.y:F1}) - no UI element hit",
                Action = MouseAction.Click.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private static SimulateMouseUiResponse CreateLongPressResult(
            MouseUiSimulationCommand parameters,
            Vector2 inputPos,
            string? targetName,
            bool hitTarget)
        {
            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = hitTarget
                    ? parameters.BypassRaycast
                        ? $"Bypass-long-pressed '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1}) via '{parameters.TargetPath}' for {parameters.Duration:F1}s"
                        : $"Long-pressed '{targetName}' at ({inputPos.x:F1}, {inputPos.y:F1}) for {parameters.Duration:F1}s"
                    : $"Long-pressed at ({inputPos.x:F1}, {inputPos.y:F1}) for {parameters.Duration:F1}s - no UI element hit",
                Action = MouseAction.LongPress.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private static SimulateMouseUiResponse CreateDragResult(
            MouseUiSimulationCommand parameters,
            Vector2 inputStart,
            Vector2 inputEnd,
            string targetName)
        {
            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = parameters.BypassRaycast
                    ? $"Bypass-dragged '{targetName}' from ({inputStart.x:F1}, {inputStart.y:F1}) to ({inputEnd.x:F1}, {inputEnd.y:F1}) via '{parameters.TargetPath}' at {parameters.DragSpeed:F0} px/s"
                    : $"Dragged '{targetName}' from ({inputStart.x:F1}, {inputStart.y:F1}) to ({inputEnd.x:F1}, {inputEnd.y:F1}) at {parameters.DragSpeed:F0} px/s",
                Action = MouseAction.Drag.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputStart.x,
                PositionY = inputStart.y,
                EndPositionX = inputEnd.x,
                EndPositionY = inputEnd.y
            };
        }

        private static SimulateMouseUiResponse CreateDragEndResult(
            MouseUiSimulationCommand parameters,
            Vector2 inputEnd,
            string targetName)
        {
            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = $"Drag ended on '{targetName}' at ({inputEnd.x:F1}, {inputEnd.y:F1}) at {parameters.DragSpeed:F0} px/s",
                Action = MouseAction.DragEnd.ToString(),
                HitGameObjectName = targetName,
                PositionX = inputEnd.x,
                PositionY = inputEnd.y
            };
        }

        private static RaycastResult? RaycastUI(Vector2 screenPosition, EventSystem eventSystem)
        {
            return UiRaycastHelper.RaycastUI(screenPosition, eventSystem);
        }

        private static bool SupportsBypassRaycast(MouseAction action)
        {
            return action == MouseAction.Click
                || action == MouseAction.LongPress
                || IsDragAction(action);
        }

        private static bool RequiresBypassTargetPath(MouseAction action)
        {
            return action == MouseAction.Click
                || action == MouseAction.LongPress
                || action == MouseAction.Drag
                || action == MouseAction.DragStart;
        }

        private static bool TryResolveGameObjectPath(
            string targetPath,
            string parameterName,
            MouseAction action,
            Vector2 inputPosition,
            out GameObject? target,
            out SimulateMouseUiResponse? failureResponse)
        {
            TargetPathLookupResult lookupResult = FindActiveGameObjectByPath(targetPath);
            target = lookupResult.Target;
            if (target != null)
            {
                failureResponse = null;
                return true;
            }

            string message = lookupResult.MatchCount == 0
                ? $"{parameterName} '{targetPath}' was not found."
                : $"{parameterName} '{targetPath}' matched {lookupResult.MatchCount} active GameObjects. Use a unique hierarchy path.";

            failureResponse = new SimulateMouseUiResponse
            {
                Success = false,
                Message = message,
                Action = action.ToString(),
                PositionX = inputPosition.x,
                PositionY = inputPosition.y
            };
            return false;
        }

        private static bool TryResolveDropTargetPath(
            MouseUiSimulationCommand parameters,
            MouseAction action,
            Vector2 inputPosition,
            out GameObject? dropTarget,
            out SimulateMouseUiResponse? failureResponse)
        {
            dropTarget = null;
            failureResponse = null;

            if (string.IsNullOrWhiteSpace(parameters.DropTargetPath))
            {
                return true;
            }

            if (!TryResolveGameObjectPath(
                parameters.DropTargetPath,
                "DropTargetPath",
                action,
                inputPosition,
                out GameObject? rawDropTarget,
                out failureResponse))
            {
                return false;
            }

            GameObject? dropHandler = ExecuteEvents.GetEventHandler<IDropHandler>(rawDropTarget!);
            if (dropHandler == null)
            {
                failureResponse = new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"DropTargetPath '{parameters.DropTargetPath}' has no drop handler.",
                    Action = action.ToString(),
                    PositionX = inputPosition.x,
                    PositionY = inputPosition.y
                };
                return false;
            }

            dropTarget = rawDropTarget;
            return true;
        }

        private static TargetPathLookupResult FindActiveGameObjectByPath(string targetPath)
        {
            string normalizedPath = targetPath.Trim().Trim('/');
            if (string.IsNullOrEmpty(normalizedPath))
            {
                return new TargetPathLookupResult(null, 0);
            }

            GameObject[] gameObjects = UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None);
            GameObject? matchedTarget = null;
            int matchCount = 0;

            foreach (GameObject gameObject in gameObjects)
            {
                if (!string.Equals(
                    GameObjectPathUtility.GetFullPath(gameObject),
                    normalizedPath,
                    StringComparison.Ordinal))
                {
                    continue;
                }

                matchCount++;
                matchedTarget = gameObject;
            }

            return matchCount == 1
                ? new TargetPathLookupResult(matchedTarget, matchCount)
                : new TargetPathLookupResult(null, matchCount);
        }

        private static RaycastResult CreateDirectRaycastResult(GameObject target)
        {
            return new RaycastResult
            {
                gameObject = target
            };
        }

        /// <summary>
        /// Provides Mouse UI Simulation Command behavior for Unity CLI Loop.
        /// </summary>
        private sealed class MouseUiSimulationCommand
        {
            private MouseUiSimulationCommand(SimulateMouseUiSchema request)
            {
                if (request == null)
                {
                    throw new ArgumentNullException(nameof(request));
                }

                Action = ToRuntimeMouseAction(request.Action);
                X = request.X;
                Y = request.Y;
                FromX = request.FromX;
                FromY = request.FromY;
                DragSpeed = request.DragSpeed;
                Duration = request.Duration;
                Button = ToRuntimeMouseButton(request.Button);
                BypassRaycast = request.BypassRaycast;
                TargetPath = request.TargetPath ?? "";
                DropTargetPath = request.DropTargetPath ?? "";
            }

            public MouseAction Action { get; }
            public float X { get; }
            public float Y { get; }
            public float FromX { get; }
            public float FromY { get; }
            public float DragSpeed { get; }
            public float Duration { get; }
            public MouseButton Button { get; }
            public bool BypassRaycast { get; }
            public string TargetPath { get; }
            public string DropTargetPath { get; }

            public static MouseUiSimulationCommand FromSchema(SimulateMouseUiSchema request)
            {
                return new MouseUiSimulationCommand(request);
            }

            private static MouseAction ToRuntimeMouseAction(UnityCliLoopMouseUiAction action)
            {
                switch (action)
                {
                    case UnityCliLoopMouseUiAction.Click:
                        return MouseAction.Click;
                    case UnityCliLoopMouseUiAction.Drag:
                        return MouseAction.Drag;
                    case UnityCliLoopMouseUiAction.DragStart:
                        return MouseAction.DragStart;
                    case UnityCliLoopMouseUiAction.DragMove:
                        return MouseAction.DragMove;
                    case UnityCliLoopMouseUiAction.DragEnd:
                        return MouseAction.DragEnd;
                    case UnityCliLoopMouseUiAction.LongPress:
                        return MouseAction.LongPress;
                    default:
                        throw new ArgumentException($"Unknown mouse UI action: {action}");
                }
            }

            private static MouseButton ToRuntimeMouseButton(UnityCliLoopMouseButton button)
            {
                switch (button)
                {
                    case UnityCliLoopMouseButton.Right:
                        return MouseButton.Right;
                    case UnityCliLoopMouseButton.Middle:
                        return MouseButton.Middle;
                    default:
                        return MouseButton.Left;
                }
            }
        }

        private readonly struct TargetPathLookupResult
        {
            public TargetPathLookupResult(GameObject? target, int matchCount)
            {
                Target = target;
                MatchCount = matchCount;
            }

            public GameObject? Target { get; }
            public int MatchCount { get; }
        }

        private readonly struct ResolvedPointerTargets
        {
            private ResolvedPointerTargets(
                GameObject? rawTarget,
                GameObject? pressTarget,
                GameObject? clickTarget,
                GameObject? target,
                SimulateMouseUiResponse? failureResponse)
            {
                RawTarget = rawTarget;
                PressTarget = pressTarget;
                ClickTarget = clickTarget;
                Target = target;
                FailureResponse = failureResponse;
            }

            public static ResolvedPointerTargets Empty { get; } =
                new(null, null, null, null, null);

            public GameObject? RawTarget { get; }
            public GameObject? PressTarget { get; }
            public GameObject? ClickTarget { get; }
            public GameObject? Target { get; }
            public SimulateMouseUiResponse? FailureResponse { get; }

            public static ResolvedPointerTargets Success(
                GameObject rawTarget,
                GameObject? pressTarget,
                GameObject? clickTarget,
                GameObject? target)
            {
                return new ResolvedPointerTargets(rawTarget, pressTarget, clickTarget, target, null);
            }

            public static ResolvedPointerTargets Failure(
                SimulateMouseUiResponse? failureResponse)
            {
                Debug.Assert(failureResponse != null, "Failure response must exist when target resolution fails.");
                return new ResolvedPointerTargets(null, null, null, null, failureResponse);
            }
        }

        private static bool IsDragAction(MouseAction action)
        {
            return action == MouseAction.Drag
                || action == MouseAction.DragStart
                || action == MouseAction.DragMove
                || action == MouseAction.DragEnd;
        }
    }
}
