#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

using RuntimeMouseButton = io.github.hatayama.UnityCliLoop.Runtime.MouseButton;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Input Recorder operations for its owning module.
    /// </summary>
    internal sealed class InputRecorderService
    {
        private readonly Key[] _defaultScanKeys =
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

        private readonly RuntimeMouseButton[] _mouseButtons =
        {
            RuntimeMouseButton.Left, RuntimeMouseButton.Right, RuntimeMouseButton.Middle
        };

        private bool _isRecording;
        private int _startFrameCount;
        private float _startTime;
        private List<InputFrameEvents> _recordedFrames = new();
        private Key[]? _cachedKeysToScan;
        private readonly HashSet<Key> _previousKeyStates = new();
        private readonly HashSet<RuntimeMouseButton> _previousButtonStates = new();

        // Reused per-frame to avoid GC allocations in OnAfterUpdate
        private readonly List<RecordedInputEvent> _frameEvents = new();
        private readonly HashSet<Key> _currentKeyStates = new();
        private readonly HashSet<RuntimeMouseButton> _currentButtonStates = new();

        public event Action? RecordingStarted;
        public event Action? RecordingStopped;

        public bool IsRecording => _isRecording;
        public string? LastAutoSavePath { get; internal set; }

        public void Initialize()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public void StartRecording(HashSet<Key>? keyFilter)
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

            // Recording captures real user input; clear simulate-mouse-input overlay
            // so it doesn't linger from a previous tool call.
            SimulateMouseInputOverlayState.Clear();

            CaptureInitialKeyStates();
            CaptureInitialButtonStates();
            EmitInitialHeldEvents();

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            InputSystem.onAfterUpdate += OnAfterUpdate;

            RecordingStarted?.Invoke();
        }

        public InputRecordingData StopRecording()
        {
            Debug.Assert(_isRecording, "Cannot stop recording when not recording");

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            _isRecording = false;
            RecordInputOverlayState.Clear();

            int totalFrames = Time.frameCount - _startFrameCount;
            float duration = Time.realtimeSinceStartup - _startTime;

            InputRecordingData data = new()            {
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

        // Call after the recording data has been saved to disk,
        // so subscribers (e.g. RecordingsEditorWindow) see the new file
        public void NotifyRecordingStopped()
        {
            RecordingStopped?.Invoke();
        }

        public void ForceStop()
        {
            if (!_isRecording)
            {
                return;
            }

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            _isRecording = false;
            RecordInputOverlayState.Clear();
            Reset();
            RecordingStopped?.Invoke();
        }

        private void Reset()
        {
            _recordedFrames = new List<InputFrameEvents>();
            _previousKeyStates.Clear();
            _previousButtonStates.Clear();
            _cachedKeysToScan = null;
        }

        private void OnAfterUpdate()
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
                NotifyRecordingStopped();
                Debug.LogWarning($"[InputRecorder] Recording auto-stopped after {RecordInputConstants.MAX_RECORDING_DURATION_SECONDS}s limit. Saved to {outputPath}");
                return;
            }

            int relativeFrame = Time.frameCount - _startFrameCount;
            _frameEvents.Clear();

            RecordKeyboardEvents(_frameEvents);
            RecordMouseButtonEvents(_frameEvents);
            RecordMouseDeltaEvents(_frameEvents);
            RecordMouseScrollEvents(_frameEvents);
            RecordMousePositionEvents(_frameEvents);

            if (_frameEvents.Count > 0)
            {
                List<RecordedInputEvent> snapshot = new(_frameEvents);
                _recordedFrames.Add(new InputFrameEvents
                {
                    Frame = relativeFrame,
                    Events = snapshot
                });
            }
        }

        private void RecordKeyboardEvents(List<RecordedInputEvent> events)
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Key[] keysToScan = _cachedKeysToScan ?? _defaultScanKeys;
            _currentKeyStates.Clear();

            for (int i = 0; i < keysToScan.Length; i++)
            {
                Key key = keysToScan[i];
                KeyControl? control = keyboard[key];
                if (control != null && control.isPressed)
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

        private void RecordMouseButtonEvents(List<RecordedInputEvent> events)
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            _currentButtonStates.Clear();

            for (int i = 0; i < _mouseButtons.Length; i++)
            {
                RuntimeMouseButton button = _mouseButtons[i];
                if (MouseButtonControlResolver.GetButtonControl(mouse, button).isPressed)
                {
                    _currentButtonStates.Add(button);
                }
            }

            foreach (RuntimeMouseButton button in _currentButtonStates)
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

            foreach (RuntimeMouseButton button in _previousButtonStates)
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
            foreach (RuntimeMouseButton button in _currentButtonStates)
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

        private static void RecordMousePositionEvents(List<RecordedInputEvent> events)
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            Vector2 position = mouse.position.ReadValue();
            events.Add(new RecordedInputEvent
            {
                Type = InputEventTypes.MOUSE_POSITION,
                Data = FormatVector2(position)
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

        private Key[] BuildKeysToScan(HashSet<Key>? keyFilter)
        {
            if (keyFilter == null || keyFilter.Count == 0)
            {
                return _defaultScanKeys;
            }

            Key[] filtered = new Key[keyFilter.Count];
            keyFilter.CopyTo(filtered);
            return filtered;
        }

        // Keys/buttons already held when recording starts need explicit DOWN events,
        // otherwise replay starts with those controls released until a state change occurs.
        private void EmitInitialHeldEvents()
        {
            List<RecordedInputEvent> events = new();

            foreach (Key key in _previousKeyStates)
            {
                events.Add(new RecordedInputEvent
                {
                    Type = InputEventTypes.KEY_DOWN,
                    Data = key.ToString()
                });
            }

            foreach (RuntimeMouseButton button in _previousButtonStates)
            {
                events.Add(new RecordedInputEvent
                {
                    Type = InputEventTypes.MOUSE_CLICK,
                    Data = button.ToString()
                });
            }

            Mouse? mouse = Mouse.current;
            if (mouse != null)
            {
                events.Add(new RecordedInputEvent
                {
                    Type = InputEventTypes.MOUSE_POSITION,
                    Data = FormatVector2(mouse.position.ReadValue())
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

        private void CaptureInitialKeyStates()
        {
            Keyboard? keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            Key[] keysToScan = _cachedKeysToScan ?? _defaultScanKeys;
            for (int i = 0; i < keysToScan.Length; i++)
            {
                KeyControl? control = keyboard[keysToScan[i]];
                if (control != null && control.isPressed)
                {
                    _previousKeyStates.Add(keysToScan[i]);
                }
            }
        }

        private void CaptureInitialButtonStates()
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            for (int i = 0; i < _mouseButtons.Length; i++)
            {
                if (MouseButtonControlResolver.GetButtonControl(mouse, _mouseButtons[i]).isPressed)
                {
                    _previousButtonStates.Add(_mouseButtons[i]);
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

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                ForceStop();
            }
        }
    }

    /// <summary>
    /// Provides Input Recorder behavior for Unity CLI Loop.
    /// </summary>
    internal static class InputRecorder
    {
        private static readonly InputRecorderService ServiceValue = new InputRecorderService();

        internal static void InitializeForEditorStartup()
        {
            ServiceValue.Initialize();
        }

        public static void AddRecordingStartedHandler(Action handler)
        {
            ServiceValue.RecordingStarted += handler;
        }

        public static void RemoveRecordingStartedHandler(Action handler)
        {
            ServiceValue.RecordingStarted -= handler;
        }

        public static void AddRecordingStoppedHandler(Action handler)
        {
            ServiceValue.RecordingStopped += handler;
        }

        public static void RemoveRecordingStoppedHandler(Action handler)
        {
            ServiceValue.RecordingStopped -= handler;
        }

        public static bool IsRecording => ServiceValue.IsRecording;

        public static string? LastAutoSavePath
        {
            get { return ServiceValue.LastAutoSavePath; }
            internal set { ServiceValue.LastAutoSavePath = value; }
        }

        public static void StartRecording(HashSet<Key>? keyFilter)
        {
            ServiceValue.StartRecording(keyFilter);
        }

        public static InputRecordingData StopRecording()
        {
            return ServiceValue.StopRecording();
        }

        public static void NotifyRecordingStopped()
        {
            ServiceValue.NotifyRecordingStopped();
        }

        public static void ForceStop()
        {
            ServiceValue.ForceStop();
        }

        internal static string FormatVector2(Vector2 v)
        {
            return InputRecorderService.FormatVector2(v);
        }

        internal static Vector2 ParseVector2(string data)
        {
            return InputRecorderService.ParseVector2(data);
        }
    }
}
#endif
