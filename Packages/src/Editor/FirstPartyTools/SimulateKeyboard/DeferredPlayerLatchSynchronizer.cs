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
