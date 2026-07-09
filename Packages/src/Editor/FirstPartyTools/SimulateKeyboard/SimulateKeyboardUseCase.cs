#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates Input System keyboard simulation for the bundled simulate-keyboard tool.
    /// </summary>
    public class SimulateKeyboardUseCase
    {
        // Wire-visible fragment of the paused preflight message; tests pin the composed string.
        public const string PausedActionDescription = "simulating keyboard input";

#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<SimulateKeyboardResponse> ExecuteAsync(
            SimulateKeyboardSchema parameters,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new SimulateKeyboardResponse
            {
                Success = false,
                Message = InputSystemPackageRequirementMessage.Format("simulate-keyboard"),
                Action = parameters.Action.ToString()
            };
#else
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            ValidationResult preflight = PlayModeToolPreflightService.RequireActiveAndNotPaused(PausedActionDescription);
            if (!preflight.IsValid)
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = preflight.ErrorMessage,
                    Action = parameters.Action.ToString()
                };
            }

            if (string.IsNullOrEmpty(parameters.Key))
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = "Key parameter is required. Examples: \"W\", \"Space\", \"LeftShift\", \"A\", \"Enter\".",
                    Action = parameters.Action.ToString()
                };
            }

            string normalizedKey = NormalizeKeyName(parameters.Key);
            if (!Enum.TryParse<Key>(normalizedKey, ignoreCase: true, out Key key) || key == Key.None)
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = $"Invalid key name: \"{parameters.Key}\". Use Input System Key enum names (e.g. \"W\", \"Space\", \"LeftShift\", \"A\", \"Enter\").",
                    Action = parameters.Action.ToString()
                };
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = "No keyboard device found in Input System. Ensure the Input System package is properly configured.",
                    Action = parameters.Action.ToString()
                };
            }

            UloopPausePointRegistry.ClearLatestHitSnapshot();

            VibeLogger.LogInfo(
                "simulate_keyboard_start",
                "Keyboard simulation started",
                new { Action = parameters.Action.ToString(), Key = parameters.Key },
                correlationId: correlationId
            );

            using InputSimulationRunInBackgroundScope runInBackgroundScope = InputSimulationRunInBackgroundScope.Enable();

            EnsureOverlayExists();

            SimulateKeyboardResponse response;

            switch (parameters.Action)
            {
                case UnityCliLoopKeyboardAction.Press:
                    response = await ExecutePress(keyboard, key, parameters.Duration, ct);
                    break;

                case UnityCliLoopKeyboardAction.KeyDown:
                    response = await ExecuteKeyDown(keyboard, key, ct);
                    break;

                case UnityCliLoopKeyboardAction.KeyUp:
                    response = await ExecuteKeyUp(keyboard, key, ct);
                    break;

                default:
                    // Only reachable when an out-of-range enum value is cast from an integer;
                    // surface as a Success=false response so the CLI treats it as a normal validation failure.
                    return new SimulateKeyboardResponse
                    {
                        Success = false,
                        Message = $"Unknown keyboard action: {parameters.Action}",
                        Action = parameters.Action.ToString()
                    };
            }

            VibeLogger.LogInfo(
                "simulate_keyboard_complete",
                $"Keyboard simulation completed: {response.Message}",
                new { Action = parameters.Action.ToString(), Success = response.Success },
                correlationId: correlationId
            );

            return response;
#endif
        }

#if ULOOP_HAS_INPUT_SYSTEM
        private static void EnsureOverlayExists()
        {
            OverlayCanvasFactory.EnsureExists();
        }

        private async Task<SimulateKeyboardResponse> ExecutePress(
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
            InputSimulationWaitOutcome waitOutcome = InputSimulationWaitOutcome.Completed;

            // The edge must be probed inside gameplay input updates: editor-tick polling can
            // miss the single frame where wasPressedThisFrame is true, and an editor-update
            // consumed press is exactly the failure gameplay code cannot see.
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            Action pressEdgeMonitor = () => pressEdgeObserved |= IsGameplayPressEdgeVisible(keyboard, key);
            InputSystem.onAfterUpdate += pressEdgeMonitor;

            try
            {
                waitOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () => KeyboardKeyState.SetKeyState(keyboard, key, true),
                    ct).ConfigureAwait(false);
                if (waitOutcome == InputSimulationWaitOutcome.Completed)
                {
                    pressWasApplied = true;
                    waitOutcome = await InputSystemUpdateHelper.WaitForPressLifetime(duration, ct)
                        .ConfigureAwait(false);
                }
            }
            finally
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                InputSystem.onAfterUpdate -= pressEdgeMonitor;
                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    ScheduleTimedOutPressCleanup(keyboard, key, pressWasApplied);
                }
                else if (pressWasApplied)
                {
                    InputSimulationWaitOutcome releaseOutcome =
                        await ReleaseKeyStateIfPossible(keyboard, key, CancellationToken.None).ConfigureAwait(false);
                    if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
                    {
                        waitOutcome = InputSimulationWaitOutcome.TimedOut;
                        ScheduleTimedOutPressCleanup(keyboard, key, false);
                    }
                    else if (waitOutcome == InputSimulationWaitOutcome.Paused)
                    {
                        KeyboardKeyState.UnregisterTransientKey(key);
                        SimulateKeyboardOverlayState.ClearPress();
                    }
                    else
                    {
                        KeyboardKeyState.UnregisterTransientKey(key);
                        await FinalizePressOverlay(ct).ConfigureAwait(false);
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
            string edgeText = pressEdgeObserved
                ? ""
                : " (press edge was not observed via wasPressedThisFrame; gameplay polling may have missed it, so retry or verify with a focused log)";
            return new SimulateKeyboardResponse
            {
                Success = true,
                Message = $"Pressed '{keyName}'{durationText}{edgeText}",
                Action = UnityCliLoopKeyboardAction.Press.ToString(),
                KeyName = keyName,
                PressEdgeObserved = pressEdgeObserved
            };
        }

        private async Task<SimulateKeyboardResponse> ExecuteKeyDown(Keyboard keyboard, Key key, CancellationToken ct)
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
            InputSimulationWaitOutcome waitOutcome = InputSimulationWaitOutcome.Completed;

            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
            Action keyDownEdgeMonitor = () => pressEdgeObserved |= IsGameplayPressEdgeVisible(keyboard, key);
            InputSystem.onAfterUpdate += keyDownEdgeMonitor;

            try
            {
                waitOutcome = await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                    () => KeyboardKeyState.SetKeyState(keyboard, key, true),
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
                    ScheduleTimedOutHeldKeyCleanup(keyboard, key, keyName, keyDownApplied);
                }
                else if (keyDownApplied && !committed)
                {
                    InputSimulationWaitOutcome rollbackOutcome =
                        await RollbackHeldKey(keyboard, key, keyName, CancellationToken.None).ConfigureAwait(false);
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
                : " (press edge was not observed via wasPressedThisFrame; gameplay polling may have missed it)";
            return new SimulateKeyboardResponse
            {
                Success = true,
                Message = $"Key '{keyName}' held down{keyDownEdgeText}",
                Action = UnityCliLoopKeyboardAction.KeyDown.ToString(),
                KeyName = keyName,
                PressEdgeObserved = pressEdgeObserved
            };
        }

        private async Task<SimulateKeyboardResponse> ExecuteKeyUp(Keyboard keyboard, Key key, CancellationToken ct)
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
                await ReleaseKeyStateIfPossible(keyboard, key, CancellationToken.None).ConfigureAwait(false);

            if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleTimedOutHeldKeyCleanup(keyboard, key, keyName, false);
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

        private static string NormalizeKeyName(string keyName)
        {
            if (string.Equals(keyName, "Return", StringComparison.OrdinalIgnoreCase))
            {
                return Key.Enter.ToString();
            }
            return keyName;
        }

        private static async Task FinalizePressOverlay(CancellationToken ct)
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

        private static async Task<InputSimulationWaitOutcome> RollbackHeldKey(
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

        private static async Task<InputSimulationWaitOutcome> ReleaseKeyStateIfPossible(
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
                ReleaseKeyStateImmediately(keyboard, key);
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

        private static void ReleaseKeyStateImmediately(Keyboard keyboard, Key key)
        {
            Debug.Assert(CanInjectKeyboardState(keyboard), "keyboard state can only be released while PlayMode has a keyboard");
            if (!CanInjectKeyboardState(keyboard))
            {
                return;
            }

            KeyboardKeyState.SetKeyState(keyboard, key, false);
            InputSystemUpdateHelper.RunExplicitUpdate(InputUpdateTypeResolver.Resolve());
        }

        private static void ScheduleTimedOutPressCleanup(Keyboard keyboard, Key key, bool pressWasApplied)
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

        private static void ScheduleTimedOutHeldKeyCleanup(Keyboard keyboard, Key key, string keyName, bool keyWasApplied)
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

        // Runs inside InputSystem.onAfterUpdate. Editor updates are excluded because a press
        // consumed there never surfaces as wasPressedThisFrame to gameplay Update polling.
        private static bool IsGameplayPressEdgeVisible(Keyboard keyboard, Key key)
        {
            if (UnityEngine.InputSystem.LowLevel.InputState.currentUpdateType == UnityEngine.InputSystem.LowLevel.InputUpdateType.Editor)
            {
                return false;
            }
            return keyboard[key].wasPressedThisFrame;
        }
#endif
    }
}
