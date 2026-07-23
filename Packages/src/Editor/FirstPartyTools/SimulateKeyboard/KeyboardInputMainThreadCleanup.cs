#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Restores keyboard device and overlay state on the Unity main thread after simulation ends.
    /// </summary>
    internal static class KeyboardInputMainThreadCleanup
    {
        /// <summary>
        /// Forces every tracked and device-pressed key up via an explicit update, then clears
        /// bookkeeping. Safe while PlayMode is paused (ReleaseAll's paused-tolerant path).
        /// </summary>
        internal static IReadOnlyList<string> ReleaseAllKeysImmediately(Keyboard keyboard)
        {
            HashSet<Key> keysToRelease = new HashSet<Key>();
            foreach (Key tracked in KeyboardKeyState.ClearTrackedKeys())
            {
                keysToRelease.Add(tracked);
            }

            if (keyboard != null)
            {
                foreach (KeyControl control in keyboard.allKeys)
                {
                    if (control != null && control.isPressed)
                    {
                        keysToRelease.Add(control.keyCode);
                    }
                }
            }

            List<string> releasedNames = new List<string>(keysToRelease.Count);
            foreach (Key key in keysToRelease)
            {
                releasedNames.Add(key.ToString());
            }

            releasedNames.Sort(System.StringComparer.Ordinal);

            if (keyboard != null && keysToRelease.Count > 0 && CanInjectKeyboardState(keyboard))
            {
                using (StateEvent.From(keyboard, out InputEventPtr eventPtr))
                {
                    foreach (Key key in keysToRelease)
                    {
                        keyboard[key].WriteValueIntoEvent(0f, eventPtr);
                    }

                    InputUpdateType updateType = InputUpdateTypeResolver.Resolve();
                    InputState.Change(keyboard, eventPtr, updateType);
                }

                InputSystemUpdateHelper.RunExplicitUpdate(InputUpdateTypeResolver.Resolve());

                // Sync wasPressedThisFrame latches that can survive a zero-state write (see ForceSync).
                foreach (Key key in keysToRelease)
                {
                    ForceSyncButtonPressLatch(keyboard, key);
                }
            }

            SimulateKeyboardOverlayState.ClearPress();
            return releasedNames;
        }

        internal static async Task FinalizePressOverlay(CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
            if (ct.IsCancellationRequested)
            {
                SimulateKeyboardOverlayState.ClearPress();
                return;
            }

            SimulateKeyboardOverlayState.ReleasePress();
            await EditorFrameWaiter.WaitFramesOrTimeoutAsync(
                1,
                UnityCliLoopConstants.EDITOR_FRAME_WAIT_TIMEOUT_MS,
                CancellationToken.None).ConfigureAwait(false);
        }

        internal static async Task<InputSimulationWaitOutcome> RollbackHeldKey(
            Keyboard keyboard,
            Key key,
            string keyName,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            InputSimulationWaitOutcome releaseOutcome =
                await ReleaseKeyStateIfPossible(keyboard, key, ct).ConfigureAwait(false);
            if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleTimedOutHeldKeyCleanup(keyboard, key, keyName, false);
                return releaseOutcome;
            }

            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
            return releaseOutcome;
        }

        internal static async Task<InputSimulationWaitOutcome> ReleaseKeyStateIfPossible(
            Keyboard keyboard,
            Key key,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            if (!CanInjectKeyboardState(keyboard))
            {
                return InputSimulationWaitOutcome.Completed;
            }

            if (EditorApplication.isPaused)
            {
                ReleaseKeyStateImmediatelyAfterPauseInterruption(keyboard, key);
                return InputSimulationWaitOutcome.Completed;
            }

            InputSimulationWaitOutcome releaseOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => KeyboardKeyState.SetKeyState(keyboard, key, false),
                ct).ConfigureAwait(false);
            if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleReleaseKeyStateImmediately(keyboard, key);
            }

            return releaseOutcome;
        }

        private static void ScheduleReleaseKeyStateImmediately(Keyboard keyboard, Key key)
        {
            ReleaseKeyStateImmediatelyOnMainThreadAsync(keyboard, key, CancellationToken.None).Forget();
        }

        private static async Task ReleaseKeyStateImmediatelyOnMainThreadAsync(
            Keyboard keyboard,
            Key key,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            ReleaseKeyStateImmediately(keyboard, key);
        }

        /// <summary>
        /// Forces a single key up via an explicit update. Used by pause-interruption cleanup
        /// after any pending apply subscription has already been disposed.
        /// </summary>
        internal static void ReleaseKeyStateImmediately(Keyboard keyboard, Key key)
        {
            Debug.Assert(CanInjectKeyboardState(keyboard), "keyboard state can only be released while PlayMode has a keyboard");
            if (!CanInjectKeyboardState(keyboard))
            {
                return;
            }

            KeyboardKeyState.SetKeyState(keyboard, key, false);
            InputSystemUpdateHelper.RunExplicitUpdate(InputUpdateTypeResolver.Resolve());
        }

        /// <summary>
        /// Pause-interruption cleanup: dispose path has already dropped pending applies; release
        /// the device key and sync Input System button press latches so resume cannot report a
        /// stale isPressed=true.
        /// </summary>
        internal static void ReleaseKeyStateImmediatelyAfterPauseInterruption(Keyboard keyboard, Key key)
        {
            ReleaseKeyStateImmediately(keyboard, key);
            if (!CanInjectKeyboardState(keyboard))
            {
                return;
            }

            ForceSyncButtonPressLatch(keyboard, key);
        }

        /// <summary>
        /// Forces a press→release transition so ButtonControl's wasPressedThisFrame latch updates.
        /// Why: Press edge monitoring calls wasPressedThisFrame, which arms m_LastUpdateWasPress.
        /// Writing zero into an already-zero state buffer is a no-op for that latch sync, so after
        /// Editor pause/resume isPressed can stay true forever while ReadValue is 0.
        /// </summary>
        private static void ForceSyncButtonPressLatch(Keyboard keyboard, Key key)
        {
            Debug.Assert(CanInjectKeyboardState(keyboard), "press latch sync requires an injectable keyboard");
            if (!CanInjectKeyboardState(keyboard))
            {
                return;
            }

            KeyboardKeyState.SetKeyState(keyboard, key, true);
            InputSystemUpdateHelper.RunExplicitUpdate(InputUpdateTypeResolver.Resolve());
            KeyboardKeyState.SetKeyState(keyboard, key, false);
            InputSystemUpdateHelper.RunExplicitUpdate(InputUpdateTypeResolver.Resolve());
        }

        internal static void ScheduleTimedOutPressCleanup(Keyboard keyboard, Key key, bool pressWasApplied)
        {
            CleanupTimedOutPressAsync(keyboard, key, pressWasApplied, CancellationToken.None).Forget();
        }

        private static async Task CleanupTimedOutPressAsync(
            Keyboard keyboard,
            Key key,
            bool pressWasApplied,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            if (pressWasApplied)
            {
                await ReleaseKeyStateIfPossible(keyboard, key, ct).ConfigureAwait(false);
            }

            KeyboardKeyState.UnregisterTransientKey(key);
            SimulateKeyboardOverlayState.ClearPress();
        }

        internal static void ScheduleTimedOutHeldKeyCleanup(
            Keyboard keyboard,
            Key key,
            string keyName,
            bool keyWasApplied)
        {
            CleanupTimedOutHeldKeyAsync(keyboard, key, keyName, keyWasApplied, CancellationToken.None).Forget();
        }

        private static async Task CleanupTimedOutHeldKeyAsync(
            Keyboard keyboard,
            Key key,
            string keyName,
            bool keyWasApplied,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            if (keyWasApplied)
            {
                await ReleaseKeyStateIfPossible(keyboard, key, ct).ConfigureAwait(false);
            }

            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
        }

        private static bool CanInjectKeyboardState(Keyboard keyboard)
        {
            return EditorApplication.isPlaying && keyboard != null;
        }
    }
}
#endif
