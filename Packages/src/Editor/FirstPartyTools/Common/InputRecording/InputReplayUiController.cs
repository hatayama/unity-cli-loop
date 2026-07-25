#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;

using RuntimeMouseButton = io.github.hatayama.UnityCliLoop.Runtime.MouseButton;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Replays pointer UI interactions through ExecuteEvents during input recording playback.
    /// </summary>
    internal sealed class InputReplayUiController
    {
        private readonly InputReplayEventProcessor _eventProcessor;
        private readonly List<BaseInputModule> _disabledInputModules = new List<BaseInputModule>();

        private bool _prevLeftButtonHeld;
        private Vector2? _previousReplayMousePosition;
        private bool _suppressIdleUiOverlay;
        private PointerEventData? _pointerData;
        private GameObject? _currentPressTarget;
        private GameObject? _currentDragTarget;
        private bool _isDragging;
        private Vector2 _pressScreenPosition;
        private float _pressTime;

        public InputReplayUiController(InputReplayEventProcessor eventProcessor)
        {
            _eventProcessor = eventProcessor;
        }

        public void ApplyUiEvents()
        {
            UiReplayFrame? replayFrame = CreateUiReplayFrame();
            if (!replayFrame.HasValue)
            {
                RestoreUiInputModules();
                return;
            }

            ApplyUiPointerActivity(replayFrame.Value);
            ApplyUiPointerRelease(replayFrame.Value);
        }

        public void Reset()
        {
            _previousReplayMousePosition = null;
            _prevLeftButtonHeld = false;
            _suppressIdleUiOverlay = false;
            _pointerData = null;
            _currentPressTarget = null;
            _currentDragTarget = null;
            _isDragging = false;
            _pressTime = 0f;
        }

        public void RestoreUiInputModules()
        {
            for (int i = 0; i < _disabledInputModules.Count; i++)
            {
                BaseInputModule module = _disabledInputModules[i];
                if (module != null)
                {
                    module.enabled = true;
                }
            }

            _disabledInputModules.Clear();
        }

        private void ApplyUiPointerActivity(UiReplayFrame replayFrame)
        {
            if (replayFrame.JustPressed)
            {
                _suppressIdleUiOverlay = false;
                _pressTime = Time.realtimeSinceStartup;
                OnUiPointerDown(replayFrame.ScreenPosition, replayFrame.EventSystem);
                SimulateMouseUiOverlayState.Update(
                    MouseAction.Click,
                    replayFrame.InputPosition,
                    null,
                    replayFrame.GameViewSize);
                SimulateMouseUiOverlayState.RequestExpandAnimation();
                return;
            }

            if (replayFrame.LeftHeld && (_currentPressTarget != null || _currentDragTarget != null))
            {
                ApplyUiPointerHold(replayFrame);
                return;
            }

            if (!_suppressIdleUiOverlay || replayFrame.MouseMoved)
            {
                // Keeping the overlay hidden until the pointer actually moves prevents release fade-out
                // from being cancelled by the next idle frame at the same position.
                _suppressIdleUiOverlay = false;
                SimulateMouseUiOverlayState.Update(
                    MouseAction.Click,
                    replayFrame.InputPosition,
                    null,
                    replayFrame.GameViewSize);
            }
        }

        private void ApplyUiPointerHold(UiReplayFrame replayFrame)
        {
            OnUiDrag(replayFrame.ScreenPosition);

            if (_isDragging)
            {
                Vector2 pressInputPos = new(
                    _pressScreenPosition.x,
                    replayFrame.GameViewSize.y - _pressScreenPosition.y);
                SimulateMouseUiOverlayState.Update(
                    MouseAction.Drag,
                    replayFrame.InputPosition,
                    pressInputPos,
                    replayFrame.GameViewSize);
                return;
            }

            float elapsed = Time.realtimeSinceStartup - _pressTime;
            if (elapsed < 0.5f)
            {
                return;
            }

            SimulateMouseUiOverlayState.Update(
                MouseAction.LongPress,
                replayFrame.InputPosition,
                null,
                replayFrame.GameViewSize);
            SimulateMouseUiOverlayState.UpdateLongPressElapsed(elapsed);
        }

        private void ApplyUiPointerRelease(UiReplayFrame replayFrame)
        {
            if (!replayFrame.JustReleased)
            {
                return;
            }

            OnUiPointerUp(replayFrame.ScreenPosition, replayFrame.EventSystem);
            _suppressIdleUiOverlay = true;
            SimulateMouseUiOverlayState.RequestDissipateAnimation();
            SimulateMouseUiOverlayState.Clear();
        }

        private UiReplayFrame? CreateUiReplayFrame()
        {
            if (!_eventProcessor.MousePosition.HasValue)
            {
                return null;
            }

            EventSystem? eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return null;
            }

            Vector2 screenPos = _eventProcessor.MousePosition.Value;
            bool mouseMoved = !_previousReplayMousePosition.HasValue
                              || _previousReplayMousePosition.Value != screenPos;
            _previousReplayMousePosition = screenPos;

            bool leftHeld = _eventProcessor.IsLeftButtonHeld();
            bool justPressed = leftHeld && !_prevLeftButtonHeld;
            bool justReleased = !leftHeld && _prevLeftButtonHeld;
            _prevLeftButtonHeld = leftHeld;
            SetUiInputModulesSuppressed(leftHeld || justReleased);

            Vector2 gameViewSize = Handles.GetMainGameViewSize();
            Vector2 inputPos = new(screenPos.x, gameViewSize.y - screenPos.y);

            return new UiReplayFrame(
                eventSystem,
                screenPos,
                inputPos,
                gameViewSize,
                leftHeld,
                justPressed,
                justReleased,
                mouseMoved);
        }

        private void OnUiPointerDown(Vector2 screenPos, EventSystem eventSystem)
        {
            RaycastResult? hit = UiRaycastHelper.RaycastUI(screenPos, eventSystem);

            _pointerData = new PointerEventData(eventSystem)
            {
                position = screenPos,
                pressPosition = screenPos,
                button = PointerEventData.InputButton.Left
            };
            _pressScreenPosition = screenPos;
            _isDragging = false;
            _currentDragTarget = null;

            if (hit == null)
            {
                _currentPressTarget = null;
                return;
            }

            GameObject rawTarget = hit.Value.gameObject;
            _pointerData.pointerCurrentRaycast = hit.Value;
            _pointerData.pointerPressRaycast = hit.Value;

            _currentPressTarget = ExecuteEvents.GetEventHandler<IPointerDownHandler>(rawTarget)
                                  ?? ExecuteEvents.GetEventHandler<IPointerClickHandler>(rawTarget);

            if (_currentPressTarget != null)
            {
                _pointerData.pointerPress = _currentPressTarget;
                _pointerData.rawPointerPress = rawTarget;
                ExecuteEvents.ExecuteHierarchy(rawTarget, _pointerData, ExecuteEvents.pointerDownHandler);
            }

            // initializePotentialDrag must fire before beginDrag per StandaloneInputModule contract
            _currentDragTarget = ExecuteEvents.GetEventHandler<IDragHandler>(rawTarget);
            if (_currentDragTarget != null)
            {
                ExecuteEvents.Execute(_currentDragTarget, _pointerData, ExecuteEvents.initializePotentialDrag);
            }
        }

        private void OnUiDrag(Vector2 screenPos)
        {
            if (_pointerData == null)
            {
                return;
            }

            Vector2 delta = screenPos - _pointerData.position;
            if (delta == Vector2.zero)
            {
                return;
            }

            _pointerData.position = screenPos;
            _pointerData.delta = delta;

            if (!_isDragging && _currentDragTarget != null)
            {
                float distance = (screenPos - _pressScreenPosition).magnitude;
                if (distance > EventSystem.current.pixelDragThreshold)
                {
                    _isDragging = true;
                    _pointerData.dragging = true;
                    _pointerData.pointerDrag = _currentDragTarget;
                    ExecuteEvents.Execute(_currentDragTarget, _pointerData, ExecuteEvents.beginDragHandler);
                }
            }

            if (_isDragging && _currentDragTarget != null)
            {
                ExecuteEvents.Execute(_currentDragTarget, _pointerData, ExecuteEvents.dragHandler);
            }
        }

        private void OnUiPointerUp(Vector2 screenPos, EventSystem eventSystem)
        {
            if (_pointerData == null)
            {
                return;
            }

            _pointerData.position = screenPos;

            if (_currentPressTarget != null)
            {
                ExecuteEvents.Execute(_currentPressTarget, _pointerData, ExecuteEvents.pointerUpHandler);
            }

            if (_isDragging && _currentDragTarget != null)
            {
                RaycastResult? dropHit = UiRaycastHelper.RaycastUI(screenPos, eventSystem);
                if (dropHit != null)
                {
                    _pointerData.pointerCurrentRaycast = dropHit.Value;
                    GameObject? dropTarget = ExecuteEvents.GetEventHandler<IDropHandler>(dropHit.Value.gameObject);
                    if (dropTarget != null)
                    {
                        ExecuteEvents.Execute(dropTarget, _pointerData, ExecuteEvents.dropHandler);
                    }
                }

                ExecuteEvents.Execute(_currentDragTarget, _pointerData, ExecuteEvents.endDragHandler);
            }
            else if (_currentPressTarget != null)
            {
                // StandaloneInputModule skips click when dragged; match that behavior
                GameObject? clickTarget = ExecuteEvents.GetEventHandler<IPointerClickHandler>(
                    _pointerData.rawPointerPress ?? _currentPressTarget);
                if (clickTarget != null)
                {
                    ExecuteEvents.Execute(clickTarget, _pointerData, ExecuteEvents.pointerClickHandler);
                }
            }

            _currentPressTarget = null;
            _currentDragTarget = null;
            _isDragging = false;
            _pointerData = null;
        }

        private void SetUiInputModulesSuppressed(bool suppressed)
        {
            if (!suppressed)
            {
                RestoreUiInputModules();
                return;
            }

            EventSystem? eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                return;
            }

            BaseInputModule[] modules = eventSystem.GetComponents<BaseInputModule>();
            for (int i = 0; i < modules.Length; i++)
            {
                BaseInputModule module = modules[i];
                if (!module.enabled)
                {
                    continue;
                }

                // Replay synthesizes ExecuteEvents directly; leaving input modules enabled
                // lets them consume the same injected Mouse.current state a second time.
                module.enabled = false;
                _disabledInputModules.Add(module);
            }
        }

        private readonly struct UiReplayFrame
        {
            public UiReplayFrame(
                EventSystem eventSystem,
                Vector2 screenPosition,
                Vector2 inputPosition,
                Vector2 gameViewSize,
                bool leftHeld,
                bool justPressed,
                bool justReleased,
                bool mouseMoved)
            {
                EventSystem = eventSystem;
                ScreenPosition = screenPosition;
                InputPosition = inputPosition;
                GameViewSize = gameViewSize;
                LeftHeld = leftHeld;
                JustPressed = justPressed;
                JustReleased = justReleased;
                MouseMoved = mouseMoved;
            }

            public EventSystem EventSystem { get; }
            public Vector2 ScreenPosition { get; }
            public Vector2 InputPosition { get; }
            public Vector2 GameViewSize { get; }
            public bool LeftHeld { get; }
            public bool JustPressed { get; }
            public bool JustReleased { get; }
            public bool MouseMoved { get; }
        }
    }
}
#endif
