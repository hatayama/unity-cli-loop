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
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                InputSystem.onAfterUpdate -= pressEdgeMonitor;
                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    KeyboardInputMainThreadCleanup.ScheduleTimedOutPressCleanup(
                        keyboard,
                        key,
                        pressWasApplied);
                }
                else if (pressWasApplied)
                {
                    InputSimulationWaitOutcome releaseOutcome =
                        await KeyboardInputMainThreadCleanup.ReleaseKeyStateIfPossible(
                            keyboard,
                            key,
                            CancellationToken.None).ConfigureAwait(false);
                    if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
                    {
                        waitOutcome = InputSimulationWaitOutcome.TimedOut;
                        KeyboardInputMainThreadCleanup.ScheduleTimedOutPressCleanup(keyboard, key, false);
                    }
                    else if (waitOutcome == InputSimulationWaitOutcome.Paused)
                    {
                        KeyboardKeyState.UnregisterTransientKey(key);
                        SimulateKeyboardOverlayState.ClearPress();
                    }
                    else
                    {
                        KeyboardKeyState.UnregisterTransientKey(key);
                        await KeyboardInputMainThreadCleanup.FinalizePressOverlay(ct).ConfigureAwait(false);
                    }
                }
                else
                {
                    KeyboardKeyState.UnregisterTransientKey(key);
                    SimulateKeyboardOverlayState.ClearPress();
                }
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

            string durationText = duration > 0f ? $" for {InputSimulationDurationFormatter.FormatSeconds(duration)}s" : "";
            string edgeText;
            if (pressEdgeObserved && pressHoldExtendedFrames > 0)
            {
                edgeText =
                    $" (release delayed {pressHoldExtendedFrames} frame(s) until wasPressedThisFrame was observed)";
            }
            else if (pressEdgeObserved)
            {
                edgeText = "";
            }
            else
            {
                edgeText =
                    " (press edge was not observed via wasPressedThisFrame; gameplay polling may have missed it, so retry or verify with a focused log)" +
                    PressEdgeDiagnosticsMessageFormatter.BuildSuffix(
                        edgeMissDiagnostics.ConsumedByUpdateType,
                        edgeMissDiagnostics.AnyDynamicUpdateObserved,
                        edgeMissDiagnostics.KeyAlreadyPressedBeforeQueue);
            }

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

        internal static async Task<SimulateKeyboardResponse> ExecuteKeyDown(
            Keyboard keyboard,
            Key key,
            CancellationToken ct)
        {
            string keyName = key.ToString();

            if (KeyboardKeyState.IsKeyHeld(key))
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = $"Key '{keyName}' is already held down. Call KeyUp first.",
                    Action = UnityCliLoopKeyboardAction.KeyDown.ToString(),
                    KeyName = keyName
                };
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
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = $"Key '{keyName}' is not currently held. Call KeyDown first.",
                    Action = UnityCliLoopKeyboardAction.KeyUp.ToString(),
                    KeyName = keyName
                };
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
                KeyName = keyName
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
