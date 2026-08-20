#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Schedules a one-shot player-update callback that ForceSyncs stale press latches after
    /// ReleaseAll/KeyUp. Why deferred: ReleaseAll often runs in an Editor update (especially
    /// while paused), so the immediate ForceSync gate can read the editor view and skip.
    /// </summary>
    internal static class DeferredPlayerLatchSynchronizer
    {
        private static readonly HashSet<Key> PendingKeys = new HashSet<Key>();
        private static Action? registeredCallback;
        private static bool playModeExitHooked;

        /// <summary>
        /// Merges keys into the pending set and registers onAfterUpdate when playing.
        /// Returns true when a callback is (or stays) registered. Why onAfterUpdate not
        /// onBeforeUpdate: ForceSync calls RunExplicitUpdate, and nesting InputSystem.Update
        /// from onBeforeUpdate of an in-flight player update is reentrant.
        /// </summary>
        internal static bool Schedule(IReadOnlyCollection<Key> keys)
        {
            if (!EditorApplication.isPlaying)
            {
                return false;
            }

            if (keys == null || keys.Count == 0)
            {
                return registeredCallback != null;
            }

            foreach (Key key in keys)
            {
                PendingKeys.Add(key);
            }

            HookPlayModeExitIfNeeded();

            if (registeredCallback != null)
            {
                return true;
            }

            registeredCallback = OnAfterUpdate;
            InputSystem.onAfterUpdate += registeredCallback;
            return true;
        }

        /// <summary>
        /// Drops a key from the pending set after a later successful KeyDown so the deferred
        /// callback will not treat that live press as a leftover latch.
        /// </summary>
        internal static void CancelPending(Key key)
        {
            if (!PendingKeys.Remove(key))
            {
                return;
            }

            if (PendingKeys.Count == 0)
            {
                Unsubscribe();
            }
        }

        internal static void ResetForTests()
        {
            ClearPendingAndUnsubscribe();
        }

        private static void HookPlayModeExitIfNeeded()
        {
            if (playModeExitHooked)
            {
                return;
            }

            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            playModeExitHooked = true;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            // Why: Enter Play Mode Options can skip domain reload, so a static onAfterUpdate
            // subscription would survive Stop and ForceSync on the next session's first player update.
            ClearPendingAndUnsubscribe();
        }

        private static void ClearPendingAndUnsubscribe()
        {
            Unsubscribe();
            PendingKeys.Clear();
        }

        private static void OnAfterUpdate()
        {
            DeferredLatchSyncTickDecision decision =
                DeferredPlayerLatchSyncDecision.Decide(InputState.currentUpdateType);
            if (!decision.ShouldSync)
            {
                Debug.Assert(!decision.ShouldUnsubscribe, "Editor ticks must keep the one-shot registration");
                return;
            }

            List<Key> keysToSync = new List<Key>(PendingKeys);
            // Why unsubscribe before ForceSync: ForceSync runs nested InputSystem.Update, which
            // would re-enter this handler and recurse if the one-shot were still registered.
            ClearPendingAndUnsubscribe();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            for (int index = 0; index < keysToSync.Count; index++)
            {
                Key key = keysToSync[index];
                // Why skip tracked holds: a later KeyDown can legitimately re-press the same
                // key after Schedule and before this callback. isPressed alone would treat that
                // live press as a stale latch, ForceSync-release it, and leave KeyboardKeyState
                // still held. After ReleaseAll/KeyUp the tracker is already cleared, so the
                // stale path still syncs.
                if (KeyboardKeyState.IsKeyHeld(key))
                {
                    continue;
                }

                if (keyboard[key].isPressed)
                {
                    KeyboardInputMainThreadCleanup.ForceSyncButtonPressLatch(keyboard, key);
                }
            }
        }

        private static void Unsubscribe()
        {
            if (registeredCallback == null)
            {
                return;
            }

            InputSystem.onAfterUpdate -= registeredCallback;
            registeredCallback = null;
        }
    }
}
#endif
