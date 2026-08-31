#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

using RuntimeMouseButton = io.github.hatayama.UnityCliLoop.Runtime.MouseButton;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Captures per-frame keyboard and mouse events from the active Input System devices.
    /// </summary>
    internal sealed class InputRecorderFrameCapture
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

        private readonly HashSet<Key> _previousKeyStates = new();
        private readonly HashSet<RuntimeMouseButton> _previousButtonStates = new();
        private readonly HashSet<Key> _currentKeyStates = new();
        private readonly HashSet<RuntimeMouseButton> _currentButtonStates = new();

        private Key[]? _cachedKeysToScan;

        public void BeginCapture(HashSet<Key>? keyFilter)
        {
            _cachedKeysToScan = BuildKeysToScan(keyFilter);
            _previousKeyStates.Clear();
            _previousButtonStates.Clear();
            CaptureInitialKeyStates();
            CaptureInitialButtonStates();
        }

        public void Reset()
        {
            _previousKeyStates.Clear();
            _previousButtonStates.Clear();
            _cachedKeysToScan = null;
        }

        public List<RecordedInputEvent> BuildInitialHeldEvents()
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
                    Data = InputRecordingVectorFormat.FormatVector2(mouse.position.ReadValue())
                });
            }

            return events;
        }

        public void CaptureFrameEvents(List<RecordedInputEvent> events)
        {
            RecordKeyboardEvents(events);
            RecordMouseButtonEvents(events);
            RecordMouseDeltaEvents(events);
            RecordMouseScrollEvents(events);
            RecordMousePositionEvents(events);
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
                Data = InputRecordingVectorFormat.FormatVector2(delta)
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
                Data = InputRecordingVectorFormat.FormatVector2(position)
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
    }
}
#endif
