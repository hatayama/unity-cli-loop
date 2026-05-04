#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.UnityCliLoop
{
    [InitializeOnLoad]
    internal static class InputReplayer
    {
        private static readonly Dictionary<string, Key> _keyLookup = BuildKeyLookup();
        private static readonly Key[] _allKeys = BuildAllKeys();
        private static readonly Dictionary<string, MouseButton> _buttonLookup = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Left", MouseButton.Left },
            { "Right", MouseButton.Right },
            { "Middle", MouseButton.Middle }
        };
        private static readonly Key[] _emptyKeys = Array.Empty<Key>();
        private static readonly MouseButton[] _emptyButtons = Array.Empty<MouseButton>();

        private static bool _isReplaying;
        private static InputRecordingData? _data;
        private static int _eventIndex;
        private static int _currentFrame;
        private static bool _loop;
        private static bool _showOverlay;
        private static readonly HashSet<Key> _replayHeldKeys = new();
        private static readonly HashSet<MouseButton> _replayHeldButtons = new();
        private static readonly List<BaseInputModule> _disabledInputModules = new();
        private static Vector2? _replayMousePosition;

        // UI replay goes through ExecuteEvents so verification does not depend on
        // GameView focus or the active input module's update timing.
        private static bool _hasMousePosition;
        private static bool _prevLeftButtonHeld;
        private static Vector2? _previousReplayMousePosition;
        private static bool _suppressIdleUiOverlay;
        private static PointerEventData? _pointerData;
        private static GameObject? _currentPressTarget;
        private static GameObject? _currentDragTarget;
        private static bool _isDragging;
        private static Vector2 _pressScreenPosition;
        private static float _pressTime;

        public static event Action? ReplayStarted;
        public static event Action? ReplayCompleted;

        public static bool IsReplaying => _isReplaying;
        public static int CurrentFrame => _currentFrame;
        public static int TotalFrames => _data?.Metadata.TotalFrames ?? 0;

        public static float Progress
        {
            get
            {
                int total = TotalFrames;
                return total > 0 ? (float)_currentFrame / total : 0f;
            }
        }

        static InputReplayer()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void StartReplay(InputRecordingData data, bool loop, bool showOverlay)
        {
            Debug.Assert(!_isReplaying, "Cannot start replay while already replaying");
            Debug.Assert(EditorApplication.isPlaying, "PlayMode must be active to start replay");
            Debug.Assert(data != null, "Recording data must not be null");

            _data = data;
            _eventIndex = 0;
            _currentFrame = 0;
            _loop = loop;
            _showOverlay = showOverlay;
            _replayHeldKeys.Clear();
            _replayHeldButtons.Clear();
            _hasMousePosition = DetectMousePositionEvents(data!);
            ResetUiReplayState();
            if (_hasMousePosition)
            {
                SimulateMouseInputOverlayState.Clear();
            }
            _isReplaying = true;

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            InputSystem.onAfterUpdate += OnAfterUpdate;

            ReplayStarted?.Invoke();
        }

        public static void StopReplay()
        {
            if (!_isReplaying)
            {
                return;
            }

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            _isReplaying = false;

            ReleaseAllHeldInputs();

            _data = null;
            _eventIndex = 0;
            _currentFrame = 0;
            _replayHeldKeys.Clear();
            _replayHeldButtons.Clear();
            RestoreUiInputModules();
            ResetUiReplayState();

            ReplayInputOverlayState.Clear();
            if (_hasMousePosition)
            {
                SimulateMouseUiOverlayState.Clear();
            }
        }

        private static void OnAfterUpdate()
        {
            if (!_isReplaying || _data == null)
            {
                return;
            }

            InputUpdateType currentUpdateType = InputState.currentUpdateType;
            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();
            if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
            {
                return;
            }

            Vector2 frameDelta = Vector2.zero;
            Vector2 frameScroll = Vector2.zero;

            CollectFrameState(ref frameDelta, ref frameScroll);
            ApplyCurrentFrameSnapshot(Keyboard.current, Mouse.current, frameDelta, frameScroll);

            if (_hasMousePosition)
            {
                ApplyUiEvents();
            }

            if (_showOverlay)
            {
                ReplayInputOverlayState.Update(_currentFrame, _data.Metadata.TotalFrames, _loop);
            }

            _currentFrame++;

            if (_eventIndex >= _data.Frames.Count && _currentFrame > _data.Metadata.TotalFrames)
            {
                if (_loop)
                {
                    ReleaseAllHeldInputs();
                    _eventIndex = 0;
                    _currentFrame = 0;
                    ResetUiReplayState();
                }
                else
                {
                    StopReplay();
                    ReplayCompleted?.Invoke();
                }
            }
        }

        private static void CollectFrameState(ref Vector2 frameDelta, ref Vector2 frameScroll)
        {
            Debug.Assert(_data != null, "_data must not be null while replaying");

            while (_eventIndex < _data!.Frames.Count && _data.Frames[_eventIndex].Frame <= _currentFrame)
            {
                InputFrameEvents frameEvents = _data.Frames[_eventIndex];
                for (int i = 0; i < frameEvents.Events.Count; i++)
                {
                    ProcessEvent(frameEvents.Events[i], ref frameDelta, ref frameScroll);
                }

                _eventIndex++;
            }
        }

        private static void ApplyCurrentFrameSnapshot(
            Keyboard? keyboard,
            Mouse? mouse,
            Vector2 frameDelta,
            Vector2 frameScroll)
        {
            if (keyboard != null)
            {
                ApplyKeyboardSnapshot(keyboard, _replayHeldKeys);
            }

            if (mouse != null)
            {
                ApplyMouseSnapshot(mouse, _replayHeldButtons, frameDelta, frameScroll, _replayMousePosition);
            }
        }

        private static void ProcessEvent(
            RecordedInputEvent evt,
            ref Vector2 frameDelta,
            ref Vector2 frameScroll)
        {
            switch (evt.Type)
            {
                case InputEventTypes.KEY_DOWN:
                    ProcessKeyDown(evt.Data);
                    break;
                case InputEventTypes.KEY_UP:
                    ProcessKeyUp(evt.Data);
                    break;
                case InputEventTypes.MOUSE_CLICK:
                    ProcessMouseClick(evt.Data);
                    break;
                case InputEventTypes.MOUSE_RELEASE:
                    ProcessMouseRelease(evt.Data);
                    break;
                case InputEventTypes.MOUSE_DELTA:
                    ProcessMouseDelta(evt.Data, ref frameDelta);
                    break;
                case InputEventTypes.MOUSE_SCROLL:
                    ProcessMouseScroll(evt.Data, ref frameScroll);
                    break;
                case InputEventTypes.MOUSE_POSITION:
                    ProcessMousePosition(evt.Data);
                    break;
            }
        }

        private static void ProcessKeyDown(string keyName)
        {
            if (!_keyLookup.TryGetValue(keyName, out Key key))
            {
                return;
            }

            _replayHeldKeys.Add(key);
            SimulateKeyboardOverlayState.AddHeldKey(keyName);
        }

        private static void ProcessKeyUp(string keyName)
        {
            if (!_keyLookup.TryGetValue(keyName, out Key key))
            {
                return;
            }

            _replayHeldKeys.Remove(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
        }

        private static void ProcessMouseClick(string buttonName)
        {
            if (!_buttonLookup.TryGetValue(buttonName, out MouseButton button))
            {
                return;
            }

            _replayHeldButtons.Add(button);
            if (!_hasMousePosition)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(button, true);
            }
        }

        private static void ProcessMouseRelease(string buttonName)
        {
            if (!_buttonLookup.TryGetValue(buttonName, out MouseButton button))
            {
                return;
            }

            _replayHeldButtons.Remove(button);
            if (!_hasMousePosition)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(button, false);
            }
        }

        private static void ProcessMouseDelta(string data, ref Vector2 frameDelta)
        {
            frameDelta = InputRecorder.ParseVector2(data);
            if (!_hasMousePosition)
            {
                SimulateMouseInputOverlayState.SetMoveDelta(frameDelta);
            }
        }

        private static void ProcessMousePosition(string data)
        {
            _replayMousePosition = InputRecorder.ParseVector2(data);
        }

        private static void ProcessMouseScroll(string data, ref Vector2 frameScroll)
        {
            if (!float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out float scrollY))
            {
                return;
            }

            frameScroll = new Vector2(0f, scrollY);
            if (!_hasMousePosition)
            {
                int direction = scrollY > 0f ? 1 : scrollY < 0f ? -1 : 0;
                SimulateMouseInputOverlayState.SetScrollDirection(direction);
            }
        }

        private static void ApplyKeyboardSnapshot(Keyboard keyboard, IReadOnlyCollection<Key> heldKeys)
        {
            InputUpdateType updateType = InputUpdateTypeResolver.Resolve();
            using (StateEvent.From(keyboard, out InputEventPtr eventPtr))
            {
                // StateEvent carries the previous frame's state; without zeroing first,
                // released keys would remain pressed until explicitly cleared.
                for (int i = 0; i < _allKeys.Length; i++)
                {
                    KeyControl? control = keyboard[_allKeys[i]];
                    if (control != null)
                    {
                        control.WriteValueIntoEvent(0f, eventPtr);
                    }
                }

                foreach (Key key in heldKeys)
                {
                    KeyControl? control = keyboard[key];
                    if (control != null)
                    {
                        control.WriteValueIntoEvent(1f, eventPtr);
                    }
                }

                InputState.Change(keyboard, eventPtr, updateType);
            }
        }

        private static void ApplyMouseSnapshot(
            Mouse mouse,
            IReadOnlyCollection<MouseButton> heldButtons,
            Vector2 delta,
            Vector2 scroll,
            Vector2? position)
        {
            InputUpdateType updateType = InputUpdateTypeResolver.Resolve();
            using (StateEvent.From(mouse, out InputEventPtr eventPtr))
            {
                mouse.leftButton.WriteValueIntoEvent(0f, eventPtr);
                mouse.rightButton.WriteValueIntoEvent(0f, eventPtr);
                mouse.middleButton.WriteValueIntoEvent(0f, eventPtr);
                mouse.delta.WriteValueIntoEvent(delta, eventPtr);
                mouse.scroll.WriteValueIntoEvent(scroll, eventPtr);

                if (position.HasValue)
                {
                    mouse.position.WriteValueIntoEvent(position.Value, eventPtr);
                }

                foreach (MouseButton button in heldButtons)
                {
                    MouseInputState.GetButtonControl(mouse, button).WriteValueIntoEvent(1f, eventPtr);
                }

                InputState.Change(mouse, eventPtr, updateType);
            }
        }

        private static void ReleaseAllHeldInputs()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null)
            {
                ApplyKeyboardSnapshot(keyboard, _emptyKeys);
            }

            Mouse? mouse = Mouse.current;
            if (mouse != null)
            {
                ApplyMouseSnapshot(mouse, _emptyButtons, Vector2.zero, Vector2.zero, null);
            }

            foreach (Key key in _replayHeldKeys)
            {
                SimulateKeyboardOverlayState.RemoveHeldKey(key.ToString());
            }

            foreach (MouseButton button in _replayHeldButtons)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(button, false);
            }

            SimulateMouseInputOverlayState.SetMoveDelta(Vector2.zero);
            SimulateMouseInputOverlayState.SetScrollDirection(0);
            _replayHeldKeys.Clear();
            _replayHeldButtons.Clear();
        }

        private static Key[] BuildAllKeys()
        {
            List<Key> keys = new List<Key>();
            foreach (Key key in Enum.GetValues(typeof(Key)))
            {
                if (key == Key.None)
                {
                    continue;
                }

                keys.Add(key);
            }

            return keys.ToArray();
        }

        private static Dictionary<string, Key> BuildKeyLookup()
        {
            Dictionary<string, Key> lookup = new Dictionary<string, Key>(StringComparer.OrdinalIgnoreCase);
            foreach (Key key in Enum.GetValues(typeof(Key)))
            {
                if (key == Key.None)
                {
                    continue;
                }

                string name = key.ToString();
                if (!lookup.ContainsKey(name))
                {
                    lookup[name] = key;
                }
            }

            return lookup;
        }

        private static void ApplyUiEvents()
        {
            if (!_replayMousePosition.HasValue)
            {
                RestoreUiInputModules();
                return;
            }

            EventSystem? eventSystem = EventSystem.current;
            if (eventSystem == null)
            {
                RestoreUiInputModules();
                return;
            }

            Vector2 screenPos = _replayMousePosition.Value;
            bool mouseMoved = !_previousReplayMousePosition.HasValue
                              || _previousReplayMousePosition.Value != screenPos;
            _previousReplayMousePosition = screenPos;

            bool leftHeld = _replayHeldButtons.Contains(MouseButton.Left);
            bool justPressed = leftHeld && !_prevLeftButtonHeld;
            bool justReleased = !leftHeld && _prevLeftButtonHeld;
            _prevLeftButtonHeld = leftHeld;
            SetUiInputModulesSuppressed(leftHeld || justReleased);

            Vector2 gameViewSize = Handles.GetMainGameViewSize();
            Vector2 inputPos = new Vector2(screenPos.x, gameViewSize.y - screenPos.y);

            if (justPressed)
            {
                _suppressIdleUiOverlay = false;
                _pressTime = Time.realtimeSinceStartup;
                OnUiPointerDown(screenPos, eventSystem);
                SimulateMouseUiOverlayState.Update(
                    MouseAction.Click, inputPos, null, _currentPressTarget?.name, gameViewSize);
                SimulateMouseUiOverlayState.RequestExpandAnimation();
            }
            else if (leftHeld && (_currentPressTarget != null || _currentDragTarget != null))
            {
                OnUiDrag(screenPos);

                if (_isDragging)
                {
                    Vector2 pressInputPos = new Vector2(_pressScreenPosition.x, gameViewSize.y - _pressScreenPosition.y);
                    SimulateMouseUiOverlayState.Update(
                        MouseAction.Drag, inputPos, pressInputPos, null, gameViewSize);
                }
                else
                {
                    float elapsed = Time.realtimeSinceStartup - _pressTime;
                    if (elapsed >= 0.5f)
                    {
                        SimulateMouseUiOverlayState.Update(
                            MouseAction.LongPress, inputPos, null, _currentPressTarget?.name, gameViewSize);
                        SimulateMouseUiOverlayState.UpdateLongPressElapsed(elapsed);
                    }
                }
            }
            else if (!_suppressIdleUiOverlay || mouseMoved)
            {
                // Keeping the overlay hidden until the pointer actually moves prevents release fade-out
                // from being cancelled by the next idle frame at the same position.
                _suppressIdleUiOverlay = false;
                SimulateMouseUiOverlayState.Update(
                    MouseAction.Click, inputPos, null, null, gameViewSize);
            }

            if (justReleased)
            {
                OnUiPointerUp(screenPos, eventSystem);
                _suppressIdleUiOverlay = true;
                SimulateMouseUiOverlayState.RequestDissipateAnimation();
                SimulateMouseUiOverlayState.Clear();
            }
        }

        private static void OnUiPointerDown(Vector2 screenPos, EventSystem eventSystem)
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

        private static void OnUiDrag(Vector2 screenPos)
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

        private static void OnUiPointerUp(Vector2 screenPos, EventSystem eventSystem)
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

        private static bool DetectMousePositionEvents(InputRecordingData data)
        {
            for (int i = 0; i < data.Frames.Count; i++)
            {
                List<RecordedInputEvent> events = data.Frames[i].Events;
                for (int j = 0; j < events.Count; j++)
                {
                    if (events[j].Type == InputEventTypes.MOUSE_POSITION)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        private static void SetUiInputModulesSuppressed(bool suppressed)
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

        private static void RestoreUiInputModules()
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

        private static void ResetUiReplayState()
        {
            _replayMousePosition = null;
            _previousReplayMousePosition = null;
            _prevLeftButtonHeld = false;
            _suppressIdleUiOverlay = false;
            _pointerData = null;
            _currentPressTarget = null;
            _currentDragTarget = null;
            _isDragging = false;
            _pressTime = 0f;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                StopReplay();
            }
        }
    }
}
#endif
