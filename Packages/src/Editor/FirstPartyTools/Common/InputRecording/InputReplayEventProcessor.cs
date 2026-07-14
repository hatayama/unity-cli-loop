#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

using RuntimeMouseButton = io.github.hatayama.UnityCliLoop.Runtime.MouseButton;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Translates recorded input events into held device state and Input System snapshots.
    /// </summary>
    internal sealed class InputReplayEventProcessor
    {
        private readonly Dictionary<string, Key> _keyLookup = BuildKeyLookup();
        private readonly Key[] _allKeys = BuildAllKeys();
        private readonly Dictionary<string, RuntimeMouseButton> _buttonLookup =
            new Dictionary<string, RuntimeMouseButton>(StringComparer.OrdinalIgnoreCase)
        {
            { "Left", RuntimeMouseButton.Left },
            { "Right", RuntimeMouseButton.Right },
            { "Middle", RuntimeMouseButton.Middle }
        };
        private readonly Key[] _emptyKeys = Array.Empty<Key>();
        private readonly RuntimeMouseButton[] _emptyButtons = Array.Empty<RuntimeMouseButton>();

        private readonly HashSet<Key> _replayHeldKeys = new HashSet<Key>();
        private readonly HashSet<RuntimeMouseButton> _replayHeldButtons = new HashSet<RuntimeMouseButton>();
        private Vector2? _replayMousePosition;

        public bool HasMousePosition { get; private set; }

        public Vector2? MousePosition => _replayMousePosition;

        public bool IsLeftButtonHeld()
        {
            return _replayHeldButtons.Contains(RuntimeMouseButton.Left);
        }

        public void InitializeForRecording(InputRecordingData data)
        {
            HasMousePosition = DetectMousePositionEvents(data);
            _replayHeldKeys.Clear();
            _replayHeldButtons.Clear();
            _replayMousePosition = null;
        }

        public void ClearHeldState()
        {
            _replayHeldKeys.Clear();
            _replayHeldButtons.Clear();
            _replayMousePosition = null;
        }

        public void ProcessEvent(
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

        public void ApplyCurrentFrameSnapshot(
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

        public void ReleaseAllHeldInputs()
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

            foreach (RuntimeMouseButton button in _replayHeldButtons)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(button, false);
            }

            SimulateMouseInputOverlayState.SetMoveDelta(Vector2.zero);
            SimulateMouseInputOverlayState.SetScrollDirection(0);
            _replayHeldKeys.Clear();
            _replayHeldButtons.Clear();
            // why: old ResetUiReplayState cleared virtual mouse position on loop restart; without this, loop iteration 2+ keeps the previous pass's last position until the next position event
            _replayMousePosition = null;
        }

        private void ProcessKeyDown(string keyName)
        {
            if (!_keyLookup.TryGetValue(keyName, out Key key))
            {
                return;
            }

            _replayHeldKeys.Add(key);
            SimulateKeyboardOverlayState.AddHeldKey(keyName);
        }

        private void ProcessKeyUp(string keyName)
        {
            if (!_keyLookup.TryGetValue(keyName, out Key key))
            {
                return;
            }

            _replayHeldKeys.Remove(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
        }

        private void ProcessMouseClick(string buttonName)
        {
            if (!_buttonLookup.TryGetValue(buttonName, out RuntimeMouseButton button))
            {
                return;
            }

            _replayHeldButtons.Add(button);
            if (!HasMousePosition)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(button, true);
            }
        }

        private void ProcessMouseRelease(string buttonName)
        {
            if (!_buttonLookup.TryGetValue(buttonName, out RuntimeMouseButton button))
            {
                return;
            }

            _replayHeldButtons.Remove(button);
            if (!HasMousePosition)
            {
                SimulateMouseInputOverlayState.SetButtonHeld(button, false);
            }
        }

        private void ProcessMouseDelta(string data, ref Vector2 frameDelta)
        {
            frameDelta = InputRecorder.ParseVector2(data);
            if (!HasMousePosition)
            {
                SimulateMouseInputOverlayState.SetMoveDelta(frameDelta);
            }
        }

        private void ProcessMousePosition(string data)
        {
            _replayMousePosition = InputRecorder.ParseVector2(data);
        }

        private void ProcessMouseScroll(string data, ref Vector2 frameScroll)
        {
            if (!float.TryParse(data, NumberStyles.Float, CultureInfo.InvariantCulture, out float scrollY))
            {
                return;
            }

            frameScroll = new Vector2(0f, scrollY);
            if (!HasMousePosition)
            {
                int direction = scrollY > 0f ? 1 : scrollY < 0f ? -1 : 0;
                SimulateMouseInputOverlayState.SetScrollDirection(direction);
            }
        }

        private void ApplyKeyboardSnapshot(Keyboard keyboard, IReadOnlyCollection<Key> heldKeys)
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

        private void ApplyMouseSnapshot(
            Mouse mouse,
            IReadOnlyCollection<RuntimeMouseButton> heldButtons,
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

                foreach (RuntimeMouseButton button in heldButtons)
                {
                    MouseButtonControlResolver.GetButtonControl(mouse, button).WriteValueIntoEvent(1f, eventPtr);
                }

                InputState.Change(mouse, eventPtr, updateType);
            }
        }

        private static Key[] BuildAllKeys()
        {
            List<Key> keys = new();
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
            Dictionary<string, Key> lookup = new(StringComparer.OrdinalIgnoreCase);
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
    }
}
#endif
