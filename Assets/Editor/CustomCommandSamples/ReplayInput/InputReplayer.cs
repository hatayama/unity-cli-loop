#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.uLoopMCP
{
    [InitializeOnLoad]
    internal static class InputReplayer
    {
        private static readonly Dictionary<string, Key> _keyLookup = BuildKeyLookup();
        private static readonly Dictionary<string, MouseButton> _buttonLookup = new(StringComparer.OrdinalIgnoreCase)
        {
            { "Left", MouseButton.Left },
            { "Right", MouseButton.Right },
            { "Middle", MouseButton.Middle }
        };

        private static bool _isReplaying;
        private static InputRecordingData? _data;
        private static int _eventIndex;
        private static int _currentFrame;
        private static bool _loop;
        private static bool _showOverlay;
        private static readonly HashSet<Key> _replayHeldKeys = new();
        private static readonly HashSet<MouseButton> _replayHeldButtons = new();

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
            _isReplaying = true;

            // Use onAfterUpdate so injected values overwrite physical mouse
            // events that were processed earlier in the same frame.
            InputSystem.onAfterUpdate -= OnAfterUpdate;
            InputSystem.onAfterUpdate += OnAfterUpdate;
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

            ReplayInputOverlayState.Clear();
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

            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;

            while (_eventIndex < _data.Frames.Count && _data.Frames[_eventIndex].Frame <= _currentFrame)
            {
                InputFrameEvents frameEvents = _data.Frames[_eventIndex];
                for (int i = 0; i < frameEvents.Events.Count; i++)
                {
                    ProcessEvent(frameEvents.Events[i], keyboard, mouse);
                }
                _eventIndex++;
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
                }
                else
                {
                    StopReplay();
                    ReplayCompleted?.Invoke();
                }
            }
        }

        private static void ProcessEvent(RecordedInputEvent evt, Keyboard? keyboard, Mouse? mouse)
        {
            switch (evt.Type)
            {
                case InputEventTypes.KEY_DOWN:
                    ProcessKeyDown(evt.Data, keyboard);
                    break;
                case InputEventTypes.KEY_UP:
                    ProcessKeyUp(evt.Data, keyboard);
                    break;
                case InputEventTypes.MOUSE_CLICK:
                    ProcessMouseClick(evt.Data, mouse);
                    break;
                case InputEventTypes.MOUSE_RELEASE:
                    ProcessMouseRelease(evt.Data, mouse);
                    break;
                case InputEventTypes.MOUSE_DELTA:
                    ProcessMouseDelta(evt.Data, mouse);
                    break;
                case InputEventTypes.MOUSE_SCROLL:
                    ProcessMouseScroll(evt.Data, mouse);
                    break;
            }
        }

        private static void ProcessKeyDown(string keyName, Keyboard? keyboard)
        {
            if (keyboard == null)
            {
                return;
            }

            if (!_keyLookup.TryGetValue(keyName, out Key key))
            {
                return;
            }

            KeyboardKeyState.SetKeyDown(key);
            KeyboardKeyState.SetKeyState(keyboard, key, true);
            _replayHeldKeys.Add(key);
            SimulateKeyboardOverlayState.AddHeldKey(keyName);
        }

        private static void ProcessKeyUp(string keyName, Keyboard? keyboard)
        {
            if (keyboard == null)
            {
                return;
            }

            if (!_keyLookup.TryGetValue(keyName, out Key key))
            {
                return;
            }

            KeyboardKeyState.SetKeyState(keyboard, key, false);
            KeyboardKeyState.SetKeyUp(key);
            _replayHeldKeys.Remove(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
        }

        private static void ProcessMouseClick(string buttonName, Mouse? mouse)
        {
            if (mouse == null)
            {
                return;
            }

            if (!_buttonLookup.TryGetValue(buttonName, out MouseButton button))
            {
                return;
            }

            MouseInputState.SetButtonDown(button);
            MouseInputState.SetButtonState(mouse, button, true);
            _replayHeldButtons.Add(button);
            SimulateMouseInputOverlayState.SetButtonHeld(button, true);
        }

        private static void ProcessMouseRelease(string buttonName, Mouse? mouse)
        {
            if (mouse == null)
            {
                return;
            }

            if (!_buttonLookup.TryGetValue(buttonName, out MouseButton button))
            {
                return;
            }

            MouseInputState.SetButtonState(mouse, button, false);
            MouseInputState.SetButtonUp(button);
            _replayHeldButtons.Remove(button);
            SimulateMouseInputOverlayState.SetButtonHeld(button, false);
        }

        private static void ProcessMouseDelta(string data, Mouse? mouse)
        {
            if (mouse == null)
            {
                return;
            }

            Vector2 delta = InputRecorder.ParseVector2(data);
            MouseInputState.SetDeltaState(mouse, delta);
            SimulateMouseInputOverlayState.SetMoveDelta(delta);
        }

        private static void ProcessMouseScroll(string data, Mouse? mouse)
        {
            if (mouse == null)
            {
                return;
            }

            if (!float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out float scrollY))
            {
                return;
            }

            Vector2 scroll = new Vector2(0f, scrollY);
            MouseInputState.SetScrollState(mouse, scroll);
            int direction = scrollY > 0f ? 1 : scrollY < 0f ? -1 : 0;
            SimulateMouseInputOverlayState.SetScrollDirection(direction);
        }

        private static void ReleaseAllHeldInputs()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard != null)
            {
                foreach (Key key in _replayHeldKeys)
                {
                    KeyboardKeyState.SetKeyState(keyboard, key, false);
                    KeyboardKeyState.SetKeyUp(key);
                    SimulateKeyboardOverlayState.RemoveHeldKey(key.ToString());
                }
            }

            Mouse? mouse = Mouse.current;
            if (mouse != null)
            {
                foreach (MouseButton button in _replayHeldButtons)
                {
                    MouseInputState.SetButtonState(mouse, button, false);
                    MouseInputState.SetButtonUp(button);
                    SimulateMouseInputOverlayState.SetButtonHeld(button, false);
                }
            }

            _replayHeldKeys.Clear();
            _replayHeldButtons.Clear();
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

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                StopReplay();
            }
        }
    }
}
