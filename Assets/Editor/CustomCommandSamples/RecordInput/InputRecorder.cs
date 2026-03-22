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
    internal static class InputRecorder
    {
        private static readonly Key[] DEFAULT_SCAN_KEYS =
        {
            Key.A, Key.B, Key.C, Key.D, Key.E, Key.F, Key.G, Key.H,
            Key.I, Key.J, Key.K, Key.L, Key.M, Key.N, Key.O, Key.P,
            Key.Q, Key.R, Key.S, Key.T, Key.U, Key.V, Key.W, Key.X,
            Key.Y, Key.Z,
            Key.Digit0, Key.Digit1, Key.Digit2, Key.Digit3, Key.Digit4,
            Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8, Key.Digit9,
            Key.Space, Key.LeftShift, Key.RightShift,
            Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt,
            Key.Tab, Key.Escape, Key.Enter, Key.Backspace,
            Key.UpArrow, Key.DownArrow, Key.LeftArrow, Key.RightArrow
        };

        private static readonly MouseButton[] MOUSE_BUTTONS =
        {
            MouseButton.Left, MouseButton.Right, MouseButton.Middle
        };

        private static bool _isRecording;
        private static int _startFrameCount;
        private static float _startTime;
        private static List<InputFrameEvents> _recordedFrames = new();
        private static Key[]? _cachedKeysToScan;
        private static readonly HashSet<Key> _previousKeyStates = new();
        private static readonly HashSet<MouseButton> _previousButtonStates = new();

        // Reused per-frame to avoid GC allocations in OnAfterUpdate
        private static readonly List<RecordedInputEvent> _frameEvents = new();
        private static readonly HashSet<Key> _currentKeyStates = new();
        private static readonly HashSet<MouseButton> _currentButtonStates = new();

        public static bool IsRecording => _isRecording;
        public static string? LastAutoSavePath { get; internal set; }

        static InputRecorder()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void StartRecording(HashSet<Key>? keyFilter)
        {
            Debug.Assert(!_isRecording, "Cannot start recording while already recording");
            Debug.Assert(EditorApplication.isPlaying, "PlayMode must be active to start recording");

            LastAutoSavePath = null;
            _cachedKeysToScan = BuildKeysToScan(keyFilter);
            _recordedFrames = new List<InputFrameEvents>();
            _previousKeyStates.Clear();
            _previousButtonStates.Clear();
            _startFrameCount = Time.frameCount;
            _startTime = Time.realtimeSinceStartup;
            _isRecording = true;

            CaptureInitialKeyStates();
            CaptureInitialButtonStates();
            EmitInitialHeldEvents();

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            InputSystem.onAfterUpdate += OnAfterUpdate;
        }

        public static InputRecordingData StopRecording()
        {
            Debug.Assert(_isRecording, "Cannot stop recording when not recording");

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            _isRecording = false;
            RecordInputOverlayState.Clear();

            int totalFrames = Time.frameCount - _startFrameCount;
            float duration = Time.realtimeSinceStartup - _startTime;

            InputRecordingData data = new InputRecordingData
            {
                Metadata = new InputRecordingMetadata
                {
                    RecordedAt = DateTime.UtcNow.ToString("o"),
                    TotalFrames = totalFrames,
                    DurationSeconds = duration
                },
                Frames = _recordedFrames
            };

            Reset();
            return data;
        }

        public static void ForceStop()
        {
            if (!_isRecording)
            {
                return;
            }

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            _isRecording = false;
            RecordInputOverlayState.Clear();
            Reset();
        }

        private static void Reset()
        {
            _recordedFrames = new List<InputFrameEvents>();
            _previousKeyStates.Clear();
            _previousButtonStates.Clear();
            _cachedKeysToScan = null;
        }

        private static void OnAfterUpdate()
        {
            if (!_isRecording)
            {
                return;
            }

            InputUpdateType currentUpdateType = InputState.currentUpdateType;
            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();
            if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
            {
                return;
            }

            float elapsed = Time.realtimeSinceStartup - _startTime;
            if (elapsed > RecordInputConstants.MAX_RECORDING_DURATION_SECONDS)
            {
                InputRecordingData data = StopRecording();
                string outputPath = InputRecordingFileHelper.ResolveOutputPath("");
                InputRecordingFileHelper.Save(data, outputPath);
                LastAutoSavePath = outputPath;
                Debug.LogWarning($"[InputRecorder] Recording auto-stopped after {RecordInputConstants.MAX_RECORDING_DURATION_SECONDS}s limit. Saved to {outputPath}");
                return;
            }

            int relativeFrame = Time.frameCount - _startFrameCount;
            _frameEvents.Clear();

            RecordKeyboardEvents(_frameEvents);
            RecordMouseButtonEvents(_frameEvents);
            RecordMouseDeltaEvents(_frameEvents);
            RecordMouseScrollEvents(_frameEvents);

            if (_frameEvents.Count > 0)
            {
                List<RecordedInputEvent> snapshot = new List<RecordedInputEvent>(_frameEvents);
                _recordedFrames.Add(new InputFrameEvents
                {
                    Frame = relativeFrame,
                    Events = snapshot
                });
            }
        }

        private static void RecordKeyboardEvents(List<RecordedInputEvent> events)
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Key[] keysToScan = _cachedKeysToScan ?? DEFAULT_SCAN_KEYS;
            _currentKeyStates.Clear();

            for (int i = 0; i < keysToScan.Length; i++)
            {
                Key key = keysToScan[i];
                if (keyboard[key].isPressed)
                {
                    _currentKeyStates.Add(key);
                }
            }

            foreach (Key key in _currentKeyStates)
            {
                if (!_previousKeyStates.Contains(key))
                {
                    events.Add(new RecordedInputEvent
                    {
                        Type = InputEventTypes.KEY_DOWN,
                        Data = key.ToString()
                    });
                }
            }

            foreach (Key key in _previousKeyStates)
            {
                if (!_currentKeyStates.Contains(key))
                {
                    events.Add(new RecordedInputEvent
                    {
                        Type = InputEventTypes.KEY_UP,
                        Data = key.ToString()
                    });
                }
            }

            _previousKeyStates.Clear();
            foreach (Key key in _currentKeyStates)
            {
                _previousKeyStates.Add(key);
            }
        }

        private static void RecordMouseButtonEvents(List<RecordedInputEvent> events)
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            _currentButtonStates.Clear();

            for (int i = 0; i < MOUSE_BUTTONS.Length; i++)
            {
                MouseButton button = MOUSE_BUTTONS[i];
                if (MouseInputState.GetButtonControl(mouse, button).isPressed)
                {
                    _currentButtonStates.Add(button);
                }
            }

            foreach (MouseButton button in _currentButtonStates)
            {
                if (!_previousButtonStates.Contains(button))
                {
                    events.Add(new RecordedInputEvent
                    {
                        Type = InputEventTypes.MOUSE_CLICK,
                        Data = button.ToString()
                    });
                }
            }

            foreach (MouseButton button in _previousButtonStates)
            {
                if (!_currentButtonStates.Contains(button))
                {
                    events.Add(new RecordedInputEvent
                    {
                        Type = InputEventTypes.MOUSE_RELEASE,
                        Data = button.ToString()
                    });
                }
            }

            _previousButtonStates.Clear();
            foreach (MouseButton button in _currentButtonStates)
            {
                _previousButtonStates.Add(button);
            }
        }

        private static void RecordMouseDeltaEvents(List<RecordedInputEvent> events)
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 delta = mouse.delta.ReadValue();
            if (delta == Vector2.zero)
            {
                return;
            }

            events.Add(new RecordedInputEvent
            {
                Type = InputEventTypes.MOUSE_DELTA,
                Data = FormatVector2(delta)
            });
        }

        private static void RecordMouseScrollEvents(List<RecordedInputEvent> events)
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 scroll = mouse.scroll.ReadValue();
            if (scroll.y == 0f)
            {
                return;
            }

            events.Add(new RecordedInputEvent
            {
                Type = InputEventTypes.MOUSE_SCROLL,
                Data = scroll.y.ToString(CultureInfo.InvariantCulture)
            });
        }

        private static Key[] BuildKeysToScan(HashSet<Key>? keyFilter)
        {
            if (keyFilter == null || keyFilter.Count == 0)
            {
                return DEFAULT_SCAN_KEYS;
            }

            Key[] filtered = new Key[keyFilter.Count];
            keyFilter.CopyTo(filtered);
            return filtered;
        }

        // Keys/buttons already held when recording starts need explicit DOWN events,
        // otherwise replay starts with those controls released until a state change occurs.
        private static void EmitInitialHeldEvents()
        {
            List<RecordedInputEvent> events = new List<RecordedInputEvent>();

            foreach (Key key in _previousKeyStates)
            {
                events.Add(new RecordedInputEvent
                {
                    Type = InputEventTypes.KEY_DOWN,
                    Data = key.ToString()
                });
            }

            foreach (MouseButton button in _previousButtonStates)
            {
                events.Add(new RecordedInputEvent
                {
                    Type = InputEventTypes.MOUSE_CLICK,
                    Data = button.ToString()
                });
            }

            if (events.Count > 0)
            {
                _recordedFrames.Add(new InputFrameEvents
                {
                    Frame = 0,
                    Events = events
                });
            }
        }

        private static void CaptureInitialKeyStates()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Key[] keysToScan = _cachedKeysToScan ?? DEFAULT_SCAN_KEYS;
            for (int i = 0; i < keysToScan.Length; i++)
            {
                if (keyboard[keysToScan[i]].isPressed)
                {
                    _previousKeyStates.Add(keysToScan[i]);
                }
            }
        }

        private static void CaptureInitialButtonStates()
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            for (int i = 0; i < MOUSE_BUTTONS.Length; i++)
            {
                if (MouseInputState.GetButtonControl(mouse, MOUSE_BUTTONS[i]).isPressed)
                {
                    _previousButtonStates.Add(MOUSE_BUTTONS[i]);
                }
            }
        }

        internal static string FormatVector2(Vector2 v)
        {
            return v.x.ToString(CultureInfo.InvariantCulture) + "," + v.y.ToString(CultureInfo.InvariantCulture);
        }

        internal static Vector2 ParseVector2(string data)
        {
            int commaIndex = data.IndexOf(',');
            if (commaIndex < 0)
            {
                return Vector2.zero;
            }

            string xStr = data.Substring(0, commaIndex);
            string yStr = data.Substring(commaIndex + 1);

            if (!float.TryParse(xStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float x))
            {
                return Vector2.zero;
            }

            if (!float.TryParse(yStr, NumberStyles.Float, CultureInfo.InvariantCulture, out float y))
            {
                return Vector2.zero;
            }

            return new Vector2(x, y);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                ForceStop();
            }
        }
    }
}
