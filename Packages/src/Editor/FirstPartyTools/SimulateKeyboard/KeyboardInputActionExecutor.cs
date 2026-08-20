#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Executes press, key-down, and key-up keyboard input actions.
    /// </summary>
    internal static class KeyboardInputActionExecutor
    {
        internal static async Task<SimulateKeyboardResponse> ExecutePress(
            Keyboard keyboard, Key key, float duration, CancellationToken ct)
        {
            SimulateKeyboardResponse? invalidDuration = CreateInvalidPressDurationResponse(key, duration);
            if (invalidDuration != null)
            {
                return invalidDuration;
            }

            string keyName = key.ToString();
            if (KeyboardKeyState.IsKeyHeld(key))
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = $"Key '{keyName}' is already held down. Call KeyUp first.",
                    Action = UnityCliLoopKeyboardAction.Press.ToString(),
                    KeyName = keyName
                };
            }

            SimulateKeyboardOverlayState.ShowPress(keyName);
            KeyboardKeyState.RegisterTransientKey(key);
            bool pressWasApplied = false;
            bool pressEdgeObserved = false;
            int pressHoldExtendedFrames = 0;
            PressEdgeMissDiagnostics edgeMissDiagnostics = new();
            InputSimulationWaitOutcome waitOutcome = InputSimulationWaitOutcome.Completed;

            // The edge must be probed inside gameplay input updates: editor-tick polling can
            // miss the single frame where wasPressedThisFrame is true, and an editor-update
            // consumed press is exactly the failure gameplay code cannot see.
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            Action pressEdgeMonitor = () =>
            {
                pressEdgeObserved |= IsGameplayPressEdgeVisible(keyboard, key);
                RecordPressEdgeMissDiagnostics(keyboard, key, edgeMissDiagnostics);
            };
            InputSystem.onAfterUpdate += pressEdgeMonitor;

            try
            {
                waitOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () =>
                    {
                        edgeMissDiagnostics.KeyAlreadyPressedBeforeQueue = keyboard[key].isPressed;
                        KeyboardKeyState.SetKeyState(keyboard, key, true);
                    },
                    ct).ConfigureAwait(false);
                if (waitOutcome == InputSimulationWaitOutcome.Completed)
                {
                    pressWasApplied = true;
                    InputSystemUpdateHelper.PressLifetimeWaitResult pressWaitResult =
                        await InputSystemUpdateHelper.WaitForPressLifetime(
                            duration,
                            () => pressEdgeObserved,
                            ct).ConfigureAwait(false);
                    waitOutcome = pressWaitResult.Outcome;
                    pressHoldExtendedFrames = pressWaitResult.ExtendedObservationFrames;
                }
            }
            finally
            {
                waitOutcome = await CleanupPressWaitAsync(
                    keyboard,
                    key,
                    pressWasApplied,
                    waitOutcome,
                    pressEdgeMonitor,
                    ct).ConfigureAwait(false);
            }

            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                return KeyboardInputSimulationResponseFactory.InterruptedKeyResult(
                    UnityCliLoopKeyboardAction.Press,
                    keyName,
                    pressEdgeObserved);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return KeyboardInputSimulationResponseFactory.TimedOutKeyResult(
                    UnityCliLoopKeyboardAction.Press,
                    keyName);
            }

            return BuildPressSuccessResponse(
                keyName,
                duration,
                pressEdgeObserved,
                pressHoldExtendedFrames,
                edgeMissDiagnostics);
        }

        private static SimulateKeyboardResponse? CreateInvalidPressDurationResponse(Key key, float duration)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = $"Duration must be non-negative, got: {duration}",
                    Action = UnityCliLoopKeyboardAction.Press.ToString(),
                    KeyName = key.ToString()
                };
            }

            if (duration > SimulateInputConstants.MaxDurationSeconds)
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message =
                        $"Duration must be {SimulateInputConstants.MaxDurationSeconds} seconds or less, got: {duration}. The unit is seconds, not milliseconds.",
                    Action = UnityCliLoopKeyboardAction.Press.ToString(),
                    KeyName = key.ToString()
                };
            }

            return null;
        }

        private static async Task<InputSimulationWaitOutcome> CleanupPressWaitAsync(
            Keyboard keyboard,
            Key key,
            bool pressWasApplied,
            InputSimulationWaitOutcome waitOutcome,
            Action pressEdgeMonitor,
            CancellationToken ct)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
            InputSystem.onAfterUpdate -= pressEdgeMonitor;
            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                KeyboardInputMainThreadCleanup.ScheduleTimedOutPressCleanup(
                    keyboard,
                    key,
                    pressWasApplied);
                return waitOutcome;
            }

            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                // Why: ApplyOnNextConfiguredUpdate already disposed any pending press
                // subscription when it returned Paused. Release + latch-sync only after
                // that dispose — a live queued edge or a stale wasPressedThisFrame latch
                // (armed by edge monitoring even when apply never committed) would both
                // look like a re-press after resume.
                KeyboardInputMainThreadCleanup.ReleaseKeyStateImmediatelyAfterPauseInterruption(
                    keyboard,
                    key);

                KeyboardKeyState.UnregisterTransientKey(key);
                SimulateKeyboardOverlayState.ClearPress();
                return waitOutcome;
            }

            if (!pressWasApplied)
            {
                KeyboardKeyState.UnregisterTransientKey(key);
                SimulateKeyboardOverlayState.ClearPress();
                return waitOutcome;
            }

            InputSimulationWaitOutcome releaseOutcome =
                await KeyboardInputMainThreadCleanup.ReleaseKeyStateIfPossible(
                    keyboard,
                    key,
                    CancellationToken.None).ConfigureAwait(false);
            if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                KeyboardInputMainThreadCleanup.ScheduleTimedOutPressCleanup(keyboard, key, false);
                return InputSimulationWaitOutcome.TimedOut;
            }

            KeyboardKeyState.UnregisterTransientKey(key);
            await KeyboardInputMainThreadCleanup.FinalizePressOverlay(ct).ConfigureAwait(false);
            return waitOutcome;
        }

        private static SimulateKeyboardResponse BuildPressSuccessResponse(
            string keyName,
            float duration,
            bool pressEdgeObserved,
            int pressHoldExtendedFrames,
            PressEdgeMissDiagnostics edgeMissDiagnostics)
        {
            string durationText = duration > 0f ? $" for {InputSimulationDurationFormatter.FormatSeconds(duration)}s" : "";
            string edgeText = BuildPressEdgeText(pressEdgeObserved, pressHoldExtendedFrames, edgeMissDiagnostics);
            return new SimulateKeyboardResponse
            {
                Success = true,
                Message = $"Pressed '{keyName}'{durationText}{edgeText}",
                Action = UnityCliLoopKeyboardAction.Press.ToString(),
                KeyName = keyName,
                PressEdgeObserved = pressEdgeObserved,
                PressHoldExtendedFrames = pressHoldExtendedFrames > 0 ? pressHoldExtendedFrames : null,
                PressEdgeConsumedByUpdateType = pressEdgeObserved ? null : edgeMissDiagnostics.ConsumedByUpdateType,
                PressEdgeAnyDynamicUpdateObserved = pressEdgeObserved ? null : edgeMissDiagnostics.AnyDynamicUpdateObserved,
                PressEdgeKeyAlreadyPressedBeforeQueue = pressEdgeObserved ? null : edgeMissDiagnostics.KeyAlreadyPressedBeforeQueue
            };
        }

        private static string BuildPressEdgeText(
            bool pressEdgeObserved,
            int pressHoldExtendedFrames,
            PressEdgeMissDiagnostics edgeMissDiagnostics)
        {
            if (pressEdgeObserved && pressHoldExtendedFrames > 0)
            {
                return
                    $" (release delayed {pressHoldExtendedFrames} frame(s) until wasPressedThisFrame was observed)";
            }

            if (pressEdgeObserved)
            {
                return "";
            }

            return
                " (press edge was not observed via wasPressedThisFrame; gameplay polling may have missed it, so retry or verify with a focused log)" +
                PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                    edgeMissDiagnostics.ConsumedByUpdateType,
                    edgeMissDiagnostics.AnyDynamicUpdateObserved,
                    edgeMissDiagnostics.KeyAlreadyPressedBeforeQueue);
        }

        internal static async Task<SimulateKeyboardResponse> ExecuteKeyDown(
            Keyboard keyboard,
            Key key,
            CancellationToken ct)
        {
            string keyName = key.ToString();

            if (KeyboardKeyState.IsKeyHeld(key))
            {
                return KeyboardInputSimulationResponseFactory.AlreadyHeldRejection(keyName, keyboard[key].isPressed);
            }

            bool keyDownApplied = false;
            bool committed = false;
            bool pressEdgeObserved = false;
            PressEdgeMissDiagnostics edgeMissDiagnostics = new();
            InputSimulationWaitOutcome waitOutcome = InputSimulationWaitOutcome.Completed;

            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            Action keyDownEdgeMonitor = () =>
            {
                pressEdgeObserved |= IsGameplayPressEdgeVisible(keyboard, key);
                RecordPressEdgeMissDiagnostics(keyboard, key, edgeMissDiagnostics);
            };
            InputSystem.onAfterUpdate += keyDownEdgeMonitor;

            try
            {
                waitOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () =>
                    {
                        edgeMissDiagnostics.KeyAlreadyPressedBeforeQueue = keyboard[key].isPressed;
                        KeyboardKeyState.SetKeyState(keyboard, key, true);
                    },
                    ct).ConfigureAwait(false);
                if (waitOutcome == InputSimulationWaitOutcome.Completed)
                {
                    keyDownApplied = true;
                    await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                    KeyboardKeyState.SetKeyDown(key);
                    SimulateKeyboardOverlayState.AddHeldKey(keyName);
                    waitOutcome = await InputSystemUpdateHelper.WaitForObservationFrames(ct)
                        .ConfigureAwait(false);
                    committed = waitOutcome == InputSimulationWaitOutcome.Completed;
                }
            }
            finally
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                InputSystem.onAfterUpdate -= keyDownEdgeMonitor;
                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    KeyboardInputMainThreadCleanup.ScheduleTimedOutHeldKeyCleanup(
                        keyboard,
                        key,
                        keyName,
                        keyDownApplied);
                }
                else if (keyDownApplied && !committed)
                {
                    InputSimulationWaitOutcome rollbackOutcome =
                        await KeyboardInputMainThreadCleanup.RollbackHeldKey(
                            keyboard,
                            key,
                            keyName,
                            CancellationToken.None).ConfigureAwait(false);
                    if (rollbackOutcome == InputSimulationWaitOutcome.TimedOut)
                    {
                        waitOutcome = InputSimulationWaitOutcome.TimedOut;
                    }
                }
            }

            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                // Apply may never have committed, but edge monitoring can still arm the
                // wasPressedThisFrame latch; sync it so resume does not report a stale press.
                if (!keyDownApplied)
                {
                    await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                    KeyboardInputMainThreadCleanup.ReleaseKeyStateImmediatelyAfterPauseInterruption(
                        keyboard,
                        key);
                }

                return KeyboardInputSimulationResponseFactory.InterruptedKeyResult(
                    UnityCliLoopKeyboardAction.KeyDown,
                    keyName,
                    pressEdgeObserved);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return KeyboardInputSimulationResponseFactory.TimedOutKeyResult(
                    UnityCliLoopKeyboardAction.KeyDown,
                    keyName);
            }

            string keyDownEdgeText = pressEdgeObserved
                ? ""
                : " (press edge was not observed via wasPressedThisFrame; gameplay polling may have missed it)" +
                  PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                      edgeMissDiagnostics.ConsumedByUpdateType,
                      edgeMissDiagnostics.AnyDynamicUpdateObserved,
                      edgeMissDiagnostics.KeyAlreadyPressedBeforeQueue);
            return new SimulateKeyboardResponse
            {
                Success = true,
                Message = $"Key '{keyName}' held down{keyDownEdgeText}",
                Action = UnityCliLoopKeyboardAction.KeyDown.ToString(),
                KeyName = keyName,
                PressEdgeObserved = pressEdgeObserved,
                PressEdgeConsumedByUpdateType = pressEdgeObserved ? null : edgeMissDiagnostics.ConsumedByUpdateType,
                PressEdgeAnyDynamicUpdateObserved = pressEdgeObserved ? null : edgeMissDiagnostics.AnyDynamicUpdateObserved,
                PressEdgeKeyAlreadyPressedBeforeQueue = pressEdgeObserved ? null : edgeMissDiagnostics.KeyAlreadyPressedBeforeQueue
            };
        }

        internal static async Task<SimulateKeyboardResponse> ExecuteKeyUp(
            Keyboard keyboard,
            Key key,
            CancellationToken ct)
        {
            string keyName = key.ToString();

            if (!KeyboardKeyState.IsKeyHeld(key))
            {
                return KeyboardInputSimulationResponseFactory.NotHeldRejection(keyName, keyboard[key].isPressed);
            }

            InputSimulationWaitOutcome releaseOutcome =
                await KeyboardInputMainThreadCleanup.ReleaseKeyStateIfPossible(
                    keyboard,
                    key,
                    CancellationToken.None).ConfigureAwait(false);

            if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                KeyboardInputMainThreadCleanup.ScheduleTimedOutHeldKeyCleanup(keyboard, key, keyName, false);
                return KeyboardInputSimulationResponseFactory.TimedOutKeyResult(
                    UnityCliLoopKeyboardAction.KeyUp,
                    keyName);
            }

            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);

            InputSimulationWaitOutcome waitOutcome = await InputSystemUpdateHelper.WaitForObservationFrames(ct)
                .ConfigureAwait(false);
            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                return KeyboardInputSimulationResponseFactory.InterruptedKeyResult(
                    UnityCliLoopKeyboardAction.KeyUp,
                    keyName,
                    null);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return KeyboardInputSimulationResponseFactory.TimedOutKeyResult(
                    UnityCliLoopKeyboardAction.KeyUp,
                    keyName);
            }

            return new SimulateKeyboardResponse
            {
                Success = true,
                Message = $"Key '{keyName}' released",
                Action = UnityCliLoopKeyboardAction.KeyUp.ToString(),
                KeyName = keyName,
                KeyStateTrackedHeld = KeyboardKeyState.IsKeyHeld(key),
                KeyStateDeviceIsPressed = keyboard[key].isPressed
            };
        }

        // Runs inside InputSystem.onAfterUpdate. Editor updates are excluded because a press
        // consumed there never surfaces as wasPressedThisFrame to gameplay Update polling.
        private static bool IsGameplayPressEdgeVisible(Keyboard keyboard, Key key)
        {
            if (InputState.currentUpdateType == InputUpdateType.Editor)
            {
                return false;
            }
            return keyboard[key].wasPressedThisFrame;
        }

        // Runs inside InputSystem.onAfterUpdate alongside the edge visibility check above.
        // Why: the root cause of an unobserved edge could not be reproduced (see Round-6
        // investigation), so this records which update type (if any) actually saw
        // wasPressedThisFrame become true, and whether a Dynamic update ran at all, to diagnose
        // the next real occurrence directly from the response instead of guessing.
        private static void RecordPressEdgeMissDiagnostics(
            Keyboard keyboard,
            Key key,
            PressEdgeMissDiagnostics diagnostics)
        {
            InputUpdateType currentUpdateType = InputState.currentUpdateType;
            if (currentUpdateType == InputUpdateType.Dynamic)
            {
                diagnostics.AnyDynamicUpdateObserved = true;
            }

            if (diagnostics.ConsumedByUpdateType == null && keyboard[key].wasPressedThisFrame)
            {
                diagnostics.ConsumedByUpdateType = currentUpdateType.ToString();
            }
        }

        // Mutable accumulator for RecordPressEdgeMissDiagnostics, captured by the onAfterUpdate
        // monitor lambda. A class (not out/ref locals) so the lambda closure can write to it
        // across repeated update callbacks without ref parameters.
        private sealed class PressEdgeMissDiagnostics
        {
            public string? ConsumedByUpdateType;
            public bool AnyDynamicUpdateObserved;
            public bool KeyAlreadyPressedBeforeQueue;
        }
    }
}
#endif
