#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.UnityCliLoop
{
    // Tool instances are created fresh per invocation, so held-key state
    // between KeyDown/KeyUp must be held statically.
    [InitializeOnLoad]
    internal static class KeyboardKeyState
    {
        private static readonly HashSet<Key> _heldKeys = new();
        private static readonly HashSet<Key> _transientKeys = new();

        public static bool IsKeyHeld(Key key) => _heldKeys.Contains(key);
        public static IReadOnlyCollection<Key> HeldKeys => _heldKeys;

        static KeyboardKeyState()
        {
            // Guard against duplicate subscriptions on domain reload
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public static void SetKeyDown(Key key)
        {
            _heldKeys.Add(key);
        }

        public static void SetKeyUp(Key key)
        {
            _heldKeys.Remove(key);
        }

        public static void RegisterTransientKey(Key key)
        {
            _transientKeys.Add(key);
        }

        public static void UnregisterTransientKey(Key key)
        {
            _transientKeys.Remove(key);
        }

        public static void Clear()
        {
            _heldKeys.Clear();
            _transientKeys.Clear();
        }

        // Keyboard keys are stored as a bitfield, so StateEvent.From captures
        // the entire keyboard state. To support simultaneous key holds, we write
        // ALL currently held keys into every event — not just the target key.
        public static void SetKeyState(Keyboard keyboard, Key key, bool pressed)
        {
            InputUpdateType updateType = InputUpdateTypeResolver.Resolve();
            using (StateEvent.From(keyboard, out InputEventPtr eventPtr))
            {
                foreach (Key heldKey in _heldKeys)
                {
                    keyboard[heldKey].WriteValueIntoEvent(1f, eventPtr);
                }

                foreach (Key transientKey in _transientKeys)
                {
                    keyboard[transientKey].WriteValueIntoEvent(1f, eventPtr);
                }

                keyboard[key].WriteValueIntoEvent(pressed ? 1f : 0f, eventPtr);
                // Updating player state directly avoids editor focus-dependent routing.
                InputState.Change(keyboard, eventPtr, updateType);
            }
        }

        public static void ReleaseAllKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                _heldKeys.Clear();
                _transientKeys.Clear();
                return;
            }

            // Single event with all keys released
            using (StateEvent.From(keyboard, out InputEventPtr eventPtr))
            {
                foreach (Key key in _heldKeys)
                {
                    keyboard[key].WriteValueIntoEvent(0f, eventPtr);
                }

                foreach (Key key in _transientKeys)
                {
                    keyboard[key].WriteValueIntoEvent(0f, eventPtr);
                }

                InputUpdateType updateType = InputUpdateTypeResolver.Resolve();
                InputState.Change(keyboard, eventPtr, updateType);
            }

            _heldKeys.Clear();
            _transientKeys.Clear();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                ReleaseAllKeys();
                SimulateKeyboardOverlayState.Clear();
            }
        }
    }
}
#endif
