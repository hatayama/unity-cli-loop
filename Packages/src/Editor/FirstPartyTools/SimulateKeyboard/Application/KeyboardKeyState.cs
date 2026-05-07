#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using UnityEditor;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Keyboard Key State operations for its owning module.
    /// </summary>
    internal sealed class KeyboardKeyStateService
    {
        private readonly HashSet<Key> _heldKeys = new HashSet<Key>();
        private readonly HashSet<Key> _transientKeys = new HashSet<Key>();

        public bool IsKeyHeld(Key key) => _heldKeys.Contains(key);
        public IReadOnlyCollection<Key> HeldKeys => _heldKeys;

        public void RegisterPlayModeCallbacks()
        {
            // Guard against duplicate subscriptions on domain reload
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public void SetKeyDown(Key key)
        {
            _heldKeys.Add(key);
        }

        public void SetKeyUp(Key key)
        {
            _heldKeys.Remove(key);
        }

        public void RegisterTransientKey(Key key)
        {
            _transientKeys.Add(key);
        }

        public void UnregisterTransientKey(Key key)
        {
            _transientKeys.Remove(key);
        }

        public void Clear()
        {
            _heldKeys.Clear();
            _transientKeys.Clear();
        }

        // Keyboard keys are stored as a bitfield, so StateEvent.From captures
        // the entire keyboard state. To support simultaneous key holds, we write
        // ALL currently held keys into every event — not just the target key.
        public void SetKeyState(Keyboard keyboard, Key key, bool pressed)
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

        public void ReleaseAllKeys()
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

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                ReleaseAllKeys();
                SimulateKeyboardOverlayState.Clear();
            }
        }
    }

    /// <summary>
    /// Stores Keyboard Key state shared by the owning workflow.
    /// </summary>
    internal static class KeyboardKeyState
    {
        private static readonly KeyboardKeyStateService ServiceValue = new KeyboardKeyStateService();

        internal static void InitializeForEditorStartup()
        {
            ServiceValue.RegisterPlayModeCallbacks();
        }

        public static bool IsKeyHeld(Key key) => ServiceValue.IsKeyHeld(key);
        public static IReadOnlyCollection<Key> HeldKeys => ServiceValue.HeldKeys;

        public static void SetKeyDown(Key key)
        {
            ServiceValue.SetKeyDown(key);
        }

        public static void SetKeyUp(Key key)
        {
            ServiceValue.SetKeyUp(key);
        }

        public static void RegisterTransientKey(Key key)
        {
            ServiceValue.RegisterTransientKey(key);
        }

        public static void UnregisterTransientKey(Key key)
        {
            ServiceValue.UnregisterTransientKey(key);
        }

        public static void Clear()
        {
            ServiceValue.Clear();
        }

        public static void SetKeyState(Keyboard keyboard, Key key, bool pressed)
        {
            ServiceValue.SetKeyState(keyboard, key, pressed);
        }

        public static void ReleaseAllKeys()
        {
            ServiceValue.ReleaseAllKeys();
        }
    }
}
#endif
