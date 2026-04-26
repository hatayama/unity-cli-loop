#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP
{
    [McpTool(Description = "Simulate mouse click, long-press, and drag on PlayMode UI elements via EventSystem screen coordinates")]
    public class SimulateMouseUiTool : AbstractUnityTool<SimulateMouseUiSchema, SimulateMouseUiResponse>
    {
        public override string ToolName => "simulate-mouse-ui";

        private const float EXPAND_DURATION = SimulateMouseUiAnimationConstants.EXPAND_DURATION;
        private const float EXPAND_START_SCALE = SimulateMouseUiAnimationConstants.EXPAND_START_SCALE;
        private const float DISSIPATE_DURATION = SimulateMouseUiAnimationConstants.DISSIPATE_DURATION;

        protected override async Task<SimulateMouseUiResponse> ExecuteAsync(
            SimulateMouseUiSchema parameters,
            CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            string correlationId = McpConstants.GenerateCorrelationId();

            if (!EditorApplication.isPlaying)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "PlayMode is not active. Use control-play-mode tool to start PlayMode first.",
                    Action = parameters.Action.ToString()
                };
            }

            EventSystem? eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "No EventSystem found in the scene. Ensure an EventSystem GameObject exists.",
                    Action = parameters.Action.ToString()
                };
            }

            if (parameters.Action != MouseAction.Click && parameters.Action != MouseAction.LongPress && parameters.DragSpeed < 0f)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"DragSpeed must be non-negative, got: {parameters.DragSpeed}",
                    Action = parameters.Action.ToString()
                };
            }

            // uGUI drag controls (ScrollRect, Slider) only respond to left-button drags
            if (IsDragAction(parameters.Action) && parameters.Button != MouseButton.Left)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"Drag actions only support Left button (uGUI ignores non-left drags), got: {parameters.Button}",
                    Action = parameters.Action.ToString()
                };
            }

            if (parameters.BypassRaycast && !SupportsBypassRaycast(parameters.Action))
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "BypassRaycast is not supported for this action.",
                    Action = parameters.Action.ToString()
                };
            }

            if (parameters.BypassRaycast &&
                RequiresBypassTargetPath(parameters.Action) &&
                string.IsNullOrWhiteSpace(parameters.TargetPath))
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "TargetPath is required when BypassRaycast is true for Click, LongPress, Drag, or DragStart.",
                    Action = parameters.Action.ToString()
                };
            }

            if (!string.IsNullOrWhiteSpace(parameters.DropTargetPath) &&
                parameters.Action != MouseAction.Drag &&
                parameters.Action != MouseAction.DragEnd)
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = "DropTargetPath supports Drag and DragEnd only.",
                    Action = parameters.Action.ToString()
                };
            }

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

            EnsureOverlayExists();

            // Single-pointer model: Click, one-shot Drag, and LongPress are invalid while a split drag is held
            if (MouseDragState.IsDragging &&
                (parameters.Action == MouseAction.Click || parameters.Action == MouseAction.Drag || parameters.Action == MouseAction.LongPress))
            {
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = $"Cannot {parameters.Action.ToString()} while a split drag is active. Call DragEnd first.",
                    Action = parameters.Action.ToString()
                };
            }

            SimulateMouseUiResponse response;

            switch (parameters.Action)
            {
                case MouseAction.Click:
                    response = await ExecuteClick(parameters, eventSystem, ct);
                    break;

                case MouseAction.Drag:
                    response = await ExecuteDragOneShot(parameters, eventSystem, ct);
                    break;

                case MouseAction.DragStart:
                    response = await ExecuteDragStart(parameters, eventSystem, ct);
                    break;

                case MouseAction.DragMove:
                    response = await ExecuteDragMove(parameters, ct);
                    break;

                case MouseAction.DragEnd:
                    response = await ExecuteDragEnd(parameters, ct);
                    break;

                case MouseAction.LongPress:
                    response = await ExecuteLongPress(parameters, eventSystem, ct);
                    break;

                default:
                    throw new ArgumentException($"Unknown mouse action: {parameters.Action}");
            }

            VibeLogger.LogInfo(
                "simulate_mouse_complete",
                $"Mouse simulation completed: {response.Message}",
                new { Action = parameters.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );

            return response;
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
            SimulateMouseUiSchema parameters, EventSystem eventSystem, CancellationToken ct)
        {
            Vector2 inputPos = new Vector2(parameters.X, parameters.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            RaycastResult? hit = parameters.BypassRaycast ? null : RaycastUI(screenPos, eventSystem);

            PointerEventData.InputButton inputButton = ToInputButton(parameters.Button);
            PointerEventData pointerData = new PointerEventData(eventSystem)
            {
                position = screenPos,
                pressPosition = screenPos,
                button = inputButton
            };

            GameObject? target = null;
            GameObject? pressTarget = null;
            GameObject? clickTarget = null;
            GameObject? rawTarget = null;

            if (parameters.BypassRaycast)
            {
                if (!TryResolveGameObjectPath(
                    parameters.TargetPath,
                    "TargetPath",
                    MouseAction.Click,
                    inputPos,
                    out rawTarget,
                    out SimulateMouseUiResponse? failureResponse))
                {
                    return failureResponse!;
                }

                RaycastResult directRaycast = CreateDirectRaycastResult(rawTarget!);
                pointerData.pointerCurrentRaycast = directRaycast;
                pointerData.pointerPressRaycast = directRaycast;

                pressTarget = ExecuteEvents.GetEventHandler<IPointerDownHandler>(rawTarget!);
                clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(rawTarget!);
                target = pressTarget ?? clickTarget;

                if (target != null)
                {
                    pointerData.pointerPress = target;
                    pointerData.rawPointerPress = rawTarget;
                }
            }
            else if (hit != null)
            {
                rawTarget = hit.Value.gameObject;
                pointerData.pointerCurrentRaycast = hit.Value;
                pointerData.pointerPressRaycast = hit.Value;

                // Execute dispatches only to the exact target; composite controls (Button with Text child) need hierarchy traversal
                pressTarget = ExecuteEvents.GetEventHandler<IPointerDownHandler>(rawTarget);
                clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(rawTarget);
                target = pressTarget ?? clickTarget;

                if (target != null)
                {
                    pointerData.pointerPress = target;
                    pointerData.rawPointerPress = rawTarget;
                }
            }

            if (parameters.BypassRaycast && target == null)
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

            SimulateMouseUiOverlayState.Update(
                MouseAction.Click, inputPos, null,
                target?.name, Handles.GetMainGameViewSize());

            await PlayExpandAnimation(ct);

            // Fire click events after expand animation so the user sees where the click lands
            if (rawTarget != null)
            {
                if (pressTarget != null)
                {
                    ExecuteEvents.ExecuteHierarchy(
                        rawTarget, pointerData, ExecuteEvents.pointerDownHandler);
                    ExecuteEvents.Execute(pressTarget, pointerData, ExecuteEvents.pointerUpHandler);
                }

                if (clickTarget != null)
                {
                    ExecuteEvents.Execute(clickTarget, pointerData, ExecuteEvents.pointerClickHandler);
                }
            }

            await PlayDissipateAnimation(ct);

            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = target != null
                    ? parameters.BypassRaycast
                        ? $"Bypass-clicked '{target.name}' at ({inputPos.x:F1}, {inputPos.y:F1}) via '{parameters.TargetPath}'"
                        : $"Clicked '{target.name}' at ({inputPos.x:F1}, {inputPos.y:F1})"
                    : $"Clicked at ({inputPos.x:F1}, {inputPos.y:F1}) - no UI element hit",
                Action = MouseAction.Click.ToString(),
                HitGameObjectName = target?.name,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<SimulateMouseUiResponse> ExecuteLongPress(
            SimulateMouseUiSchema parameters, EventSystem eventSystem, CancellationToken ct)
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

            Vector2 inputPos = new Vector2(parameters.X, parameters.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            RaycastResult? hit = parameters.BypassRaycast ? null : RaycastUI(screenPos, eventSystem);

            PointerEventData.InputButton inputButton = ToInputButton(parameters.Button);
            PointerEventData pointerData = new PointerEventData(eventSystem)
            {
                position = screenPos,
                pressPosition = screenPos,
                button = inputButton
            };

            GameObject? target = null;
            GameObject? rawTarget = null;

            if (parameters.BypassRaycast)
            {
                if (!TryResolveGameObjectPath(
                    parameters.TargetPath,
                    "TargetPath",
                    MouseAction.LongPress,
                    inputPos,
                    out rawTarget,
                    out SimulateMouseUiResponse? failureResponse))
                {
                    return failureResponse!;
                }

                RaycastResult directRaycast = CreateDirectRaycastResult(rawTarget!);
                pointerData.pointerCurrentRaycast = directRaycast;
                pointerData.pointerPressRaycast = directRaycast;

                target = ExecuteEvents.GetEventHandler<IPointerDownHandler>(rawTarget!)
                         ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(rawTarget!);

                if (target != null)
                {
                    pointerData.pointerPress = target;
                    pointerData.rawPointerPress = rawTarget;
                }
            }
            else if (hit != null)
            {
                rawTarget = hit.Value.gameObject;
                pointerData.pointerCurrentRaycast = hit.Value;
                pointerData.pointerPressRaycast = hit.Value;

                target = ExecuteEvents.GetEventHandler<IPointerDownHandler>(rawTarget)
                         ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(rawTarget);

                if (target != null)
                {
                    pointerData.pointerPress = target;
                    pointerData.rawPointerPress = rawTarget;
                }
            }

            if (parameters.BypassRaycast && target == null)
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

            SimulateMouseUiOverlayState.Update(
                MouseAction.LongPress, inputPos, null,
                target?.name, Handles.GetMainGameViewSize());

            await PlayExpandAnimation(ct);

            if (rawTarget != null && target != null)
            {
                ExecuteEvents.ExecuteHierarchy(
                    rawTarget, pointerData, ExecuteEvents.pointerDownHandler);
            }

            try
            {
                // Hold for Duration seconds, updating elapsed time each frame for overlay display
                float startTime = Time.realtimeSinceStartup;
                float elapsed = 0f;
                while (elapsed < parameters.Duration)
                {
                    SimulateMouseUiOverlayState.UpdateLongPressElapsed(elapsed);
                    await EditorDelay.DelayFrame(1, ct);
                    elapsed = Time.realtimeSinceStartup - startTime;
                }
                SimulateMouseUiOverlayState.UpdateLongPressElapsed(parameters.Duration);
            }
            finally
            {
                // Ensure pointerUp fires even if the hold loop is cancelled
                if (rawTarget != null && target != null)
                {
                    ExecuteEvents.Execute(target, pointerData, ExecuteEvents.pointerUpHandler);
                }
            }

            await PlayDissipateAnimation(ct);

            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = target != null
                    ? parameters.BypassRaycast
                        ? $"Bypass-long-pressed '{target.name}' at ({inputPos.x:F1}, {inputPos.y:F1}) via '{parameters.TargetPath}' for {parameters.Duration:F1}s"
                        : $"Long-pressed '{target.name}' at ({inputPos.x:F1}, {inputPos.y:F1}) for {parameters.Duration:F1}s"
                    : $"Long-pressed at ({inputPos.x:F1}, {inputPos.y:F1}) for {parameters.Duration:F1}s - no UI element hit",
                Action = MouseAction.LongPress.ToString(),
                HitGameObjectName = target?.name,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private PointerEventData InitiateDrag(
            EventSystem eventSystem,
            Vector2 screenPos,
            RaycastResult raycastResult,
            GameObject dragTarget,
            PointerEventData.InputButton inputButton)
        {
            PointerEventData pointerData = new PointerEventData(eventSystem)
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
            SimulateMouseUiSchema parameters, EventSystem eventSystem, CancellationToken ct)
        {
            Vector2 inputStart = new Vector2(parameters.FromX, parameters.FromY);
            Vector2 inputEnd = new Vector2(parameters.X, parameters.Y);
            Vector2 screenStart = InputToScreen(inputStart);
            Vector2 screenEnd = InputToScreen(inputEnd);
            RaycastResult? hit = parameters.BypassRaycast ? null : RaycastUI(screenStart, eventSystem);
            RaycastResult startRaycast = new RaycastResult();
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
                await PlayExpandAnimation(ct);
                await PlayDissipateAnimation(ct);

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

            SimulateMouseUiOverlayState.Update(
                MouseAction.Drag, inputStart, inputStart, target.name, Handles.GetMainGameViewSize());

            try
            {
                await PlayExpandAnimation(ct);
                await InterpolateDragPosition(pointerData, target, screenEnd, parameters.DragSpeed, ct);
                await EditorDelay.DelayFrame(1, ct);
            }
            finally
            {
                FinalizeDrag(pointerData, target, explicitDropTarget);
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.Drag, inputEnd, inputStart, target.name, Handles.GetMainGameViewSize());

            await PlayDissipateAnimation(ct);

            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = parameters.BypassRaycast
                    ? $"Bypass-dragged '{target.name}' from ({inputStart.x:F1}, {inputStart.y:F1}) to ({inputEnd.x:F1}, {inputEnd.y:F1}) via '{parameters.TargetPath}' at {parameters.DragSpeed:F0} px/s"
                    : $"Dragged '{target.name}' from ({inputStart.x:F1}, {inputStart.y:F1}) to ({inputEnd.x:F1}, {inputEnd.y:F1}) at {parameters.DragSpeed:F0} px/s",
                Action = MouseAction.Drag.ToString(),
                HitGameObjectName = target.name,
                PositionX = inputStart.x,
                PositionY = inputStart.y,
                EndPositionX = inputEnd.x,
                EndPositionY = inputEnd.y
            };
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
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(pointerData, results);

            if (results.Count > 0)
            {
                pointerData.pointerCurrentRaycast = results[0];
                return;
            }

            // Same Canvas-space fallback as RaycastUI for scaled Game view
            RaycastResult? fallback = UiRaycastHelper.RaycastCanvasSpace(pointerData.position);
            pointerData.pointerCurrentRaycast = fallback ?? new RaycastResult();
        }

        private async Task InterpolateDragPosition(
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
                await EditorDelay.DelayFrame(1, ct);

                Vector2 previousPosition = pointerData.position;
                pointerData.position = endPos;
                pointerData.delta = endPos - previousPosition;
                ExecuteEvents.Execute(target, pointerData, ExecuteEvents.dragHandler);

                SimulateMouseUiOverlayState.UpdatePosition(ScreenToInput(endPos));
            }
            else
            {
                float startTime = Time.realtimeSinceStartup;
                float t;

                do
                {
                    await EditorDelay.DelayFrame(1, ct);

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
            }
        }

        private async Task<SimulateMouseUiResponse> ExecuteDragStart(
            SimulateMouseUiSchema parameters, EventSystem eventSystem, CancellationToken ct)
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

            Vector2 inputPos = new Vector2(parameters.X, parameters.Y);
            Vector2 screenPos = InputToScreen(inputPos);
            RaycastResult? hit = parameters.BypassRaycast ? null : RaycastUI(screenPos, eventSystem);
            RaycastResult startRaycast = new RaycastResult();
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
                await PlayExpandAnimation(ct);
                await PlayDissipateAnimation(ct);

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

            SimulateMouseUiOverlayState.Update(
                MouseAction.DragStart, inputPos, inputPos, target.name, Handles.GetMainGameViewSize());

            bool animationCompleted = false;
            try
            {
                await PlayExpandAnimation(ct);
                animationCompleted = true;
            }
            finally
            {
                // Cancellation during animation leaves beginDrag dispatched; clean up
                if (!animationCompleted)
                {
                    FinalizeDrag(pointerData, target, null);
                    MouseDragState.Clear();
                }
            }

            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = $"Drag started on '{target.name}' at ({inputPos.x:F1}, {inputPos.y:F1})",
                Action = MouseAction.DragStart.ToString(),
                HitGameObjectName = target.name,
                PositionX = inputPos.x,
                PositionY = inputPos.y
            };
        }

        private async Task<SimulateMouseUiResponse> ExecuteDragMove(
            SimulateMouseUiSchema parameters, CancellationToken ct)
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

            Vector2 inputEnd = new Vector2(parameters.X, parameters.Y);
            Vector2 screenEnd = InputToScreen(inputEnd);

            SimulateMouseUiOverlayState.Update(
                MouseAction.DragMove,
                ScreenToInput(MouseDragState.PointerData!.position),
                SimulateMouseUiOverlayState.DragStartPosition,
                MouseDragState.Target!.name, Handles.GetMainGameViewSize());

            // Cancellation leaves drag state intact so the user can continue with DragMove/DragEnd
            await InterpolateDragPosition(
                MouseDragState.PointerData!, MouseDragState.Target!, screenEnd,
                parameters.DragSpeed, ct);

            SimulateMouseUiOverlayState.AddWaypoint(inputEnd);

            return new SimulateMouseUiResponse
            {
                Success = true,
                Message = $"Drag moved on '{MouseDragState.Target!.name}' to ({inputEnd.x:F1}, {inputEnd.y:F1}) at {parameters.DragSpeed:F0} px/s",
                Action = MouseAction.DragMove.ToString(),
                HitGameObjectName = MouseDragState.Target.name,
                PositionX = inputEnd.x,
                PositionY = inputEnd.y
            };
        }

        private async Task<SimulateMouseUiResponse> ExecuteDragEnd(
            SimulateMouseUiSchema parameters, CancellationToken ct)
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

            Vector2 inputEnd = new Vector2(parameters.X, parameters.Y);
            Vector2 screenEnd = InputToScreen(inputEnd);
            string targetName = MouseDragState.Target!.name;
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
                ScreenToInput(MouseDragState.PointerData!.position),
                SimulateMouseUiOverlayState.DragStartPosition,
                targetName, Handles.GetMainGameViewSize());

            try
            {
                await InterpolateDragPosition(
                    MouseDragState.PointerData!, MouseDragState.Target!, screenEnd,
                    parameters.DragSpeed, ct);
                await EditorDelay.DelayFrame(1, ct);
            }
            finally
            {
                FinalizeDrag(MouseDragState.PointerData!, MouseDragState.Target!, explicitDropTarget);
                MouseDragState.Clear();
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.DragEnd, inputEnd, null, targetName, Handles.GetMainGameViewSize());

            await PlayDissipateAnimation(ct);

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

        private static async Task PlayExpandAnimation(CancellationToken ct)
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
                await EditorDelay.DelayFrame(1, ct);
                elapsed = Time.realtimeSinceStartup - startTime;
            }
            overlay.SetCursorScale(1f);
        }

        private static async Task PlayDissipateAnimation(CancellationToken ct)
        {
            SimulateMouseUiOverlay overlay = OverlayCanvasFactory.VisualizationCanvas.MouseUiOverlay;

            float startTime = Time.realtimeSinceStartup;
            float elapsed = 0f;
            while (elapsed < DISSIPATE_DURATION)
            {
                float t = elapsed / DISSIPATE_DURATION;
                overlay.SetCursorScale(Mathf.Lerp(1f, 0f, t));
                overlay.SetAlpha(Mathf.Lerp(1f, 0f, t));
                await EditorDelay.DelayFrame(1, ct);
                elapsed = Time.realtimeSinceStartup - startTime;
            }
            overlay!.SetCursorScale(0f);
            overlay!.SetAlpha(0f);
            SimulateMouseUiOverlayState.Clear();
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
            SimulateMouseUiSchema parameters,
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

        private static bool IsDragAction(MouseAction action)
        {
            return action == MouseAction.Drag
                || action == MouseAction.DragStart
                || action == MouseAction.DragMove
                || action == MouseAction.DragEnd;
        }
    }
}
