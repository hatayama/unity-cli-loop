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
    public class SimulateKeyboardUseCase : IUnityCliLoopKeyboardSimulationService
    {
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<UnityCliLoopKeyboardSimulationResult> SimulateKeyboardAsync(
            UnityCliLoopKeyboardSimulationRequest request,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new UnityCliLoopKeyboardSimulationResult
            {
                Success = false,
                Message = "simulate-keyboard requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.",
                Action = request.Action.ToString()
            };
#else
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            if (!EditorApplication.isPlaying)
            {
                return new UnityCliLoopKeyboardSimulationResult
                {
                    Success = false,
                    Message = "PlayMode is not active. Use control-play-mode tool to start PlayMode first.",
                    Action = request.Action.ToString()
                };
            }

            if (EditorApplication.isPaused)
            {
                return new UnityCliLoopKeyboardSimulationResult
                {
                    Success = false,
                    Message = "PlayMode is paused. Resume PlayMode before simulating keyboard input.",
                    Action = request.Action.ToString()
                };
            }

            if (string.IsNullOrEmpty(request.Key))
            {
                return new UnityCliLoopKeyboardSimulationResult
                {
                    Success = false,
                    Message = "Key parameter is required. Examples: \"W\", \"Space\", \"LeftShift\", \"A\", \"Enter\".",
                    Action = request.Action.ToString()
                };
            }

            string normalizedKey = NormalizeKeyName(request.Key);
            if (!Enum.TryParse<Key>(normalizedKey, ignoreCase: true, out Key key) || key == Key.None)
            {
                return new UnityCliLoopKeyboardSimulationResult
                {
                    Success = false,
                    Message = $"Invalid key name: \"{request.Key}\". Use Input System Key enum names (e.g. \"W\", \"Space\", \"LeftShift\", \"A\", \"Enter\").",
                    Action = request.Action.ToString()
                };
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return new UnityCliLoopKeyboardSimulationResult
                {
                    Success = false,
                    Message = "No keyboard device found in Input System. Ensure the Input System package is properly configured.",
                    Action = request.Action.ToString()
                };
            }

            UloopPausePointRegistry.ClearLatestHitSnapshot();

            VibeLogger.LogInfo(
                "simulate_keyboard_start",
                "Keyboard simulation started",
                new { Action = request.Action.ToString(), Key = request.Key },
                correlationId: correlationId
            );

            using InputSimulationRunInBackgroundScope runInBackgroundScope = InputSimulationRunInBackgroundScope.Enable();

            EnsureOverlayExists();

            UnityCliLoopKeyboardSimulationResult response;

            switch (request.Action)
            {
                case UnityCliLoopKeyboardAction.Press:
                    response = await ExecutePress(keyboard, key, request.Duration, ct);
                    break;

                case UnityCliLoopKeyboardAction.KeyDown:
                    response = await ExecuteKeyDown(keyboard, key, ct);
                    break;

                case UnityCliLoopKeyboardAction.KeyUp:
                    response = await ExecuteKeyUp(keyboard, key, ct);
                    break;

                default:
                    throw new ArgumentException($"Unknown keyboard action: {request.Action}");
            }

            VibeLogger.LogInfo(
                "simulate_keyboard_complete",
                $"Keyboard simulation completed: {response.Message}",
                new { Action = request.Action.ToString(), Success = response.Success },
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

        private async Task<UnityCliLoopKeyboardSimulationResult> ExecutePress(
            Keyboard keyboard, Key key, float duration, CancellationToken ct)
        {
            if (duration < 0f || float.IsNaN(duration) || float.IsInfinity(duration))
            {
                return new UnityCliLoopKeyboardSimulationResult
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
                return new UnityCliLoopKeyboardSimulationResult
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
            InputSimulationWaitOutcome waitOutcome = InputSimulationWaitOutcome.Completed;

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
                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    ScheduleTimedOutPressCleanup(keyboard, key, pressWasApplied);
                }
                else if (pressWasApplied)
                {
                    InputSimulationWaitOutcome releaseOutcome =
                        await ReleaseKeyStateIfPossible(keyboard, key).ConfigureAwait(false);
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
                return InterruptedPressResult(keyName);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return TimedOutKeyResult(UnityCliLoopKeyboardAction.Press, keyName);
            }

            string durationText = duration > 0f ? $" for {InputSimulationDurationFormatter.FormatSeconds(duration)}s" : "";
            return new UnityCliLoopKeyboardSimulationResult
            {
                Success = true,
                Message = $"Pressed '{keyName}'{durationText}",
                Action = UnityCliLoopKeyboardAction.Press.ToString(),
                KeyName = keyName
            };
        }

        private async Task<UnityCliLoopKeyboardSimulationResult> ExecuteKeyDown(Keyboard keyboard, Key key, CancellationToken ct)
        {
            string keyName = key.ToString();

            if (KeyboardKeyState.IsKeyHeld(key))
            {
                return new UnityCliLoopKeyboardSimulationResult
                {
                    Success = false,
                    Message = $"Key '{keyName}' is already held down. Call KeyUp first.",
                    Action = UnityCliLoopKeyboardAction.KeyDown.ToString(),
                    KeyName = keyName
                };
            }

            bool keyDownApplied = false;
            bool committed = false;
            InputSimulationWaitOutcome waitOutcome = InputSimulationWaitOutcome.Completed;

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
                if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
                {
                    ScheduleTimedOutHeldKeyCleanup(keyboard, key, keyName, keyDownApplied);
                }
                else if (keyDownApplied && !committed)
                {
                    InputSimulationWaitOutcome rollbackOutcome =
                        await RollbackHeldKey(keyboard, key, keyName).ConfigureAwait(false);
                    if (rollbackOutcome == InputSimulationWaitOutcome.TimedOut)
                    {
                        waitOutcome = InputSimulationWaitOutcome.TimedOut;
                    }
                }
            }

            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                return InterruptedKeyResult(UnityCliLoopKeyboardAction.KeyDown, keyName);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return TimedOutKeyResult(UnityCliLoopKeyboardAction.KeyDown, keyName);
            }

            return new UnityCliLoopKeyboardSimulationResult
            {
                Success = true,
                Message = $"Key '{keyName}' held down",
                Action = UnityCliLoopKeyboardAction.KeyDown.ToString(),
                KeyName = keyName
            };
        }

        private async Task<UnityCliLoopKeyboardSimulationResult> ExecuteKeyUp(Keyboard keyboard, Key key, CancellationToken ct)
        {
            string keyName = key.ToString();

            if (!KeyboardKeyState.IsKeyHeld(key))
            {
                return new UnityCliLoopKeyboardSimulationResult
                {
                    Success = false,
                    Message = $"Key '{keyName}' is not currently held. Call KeyDown first.",
                    Action = UnityCliLoopKeyboardAction.KeyUp.ToString(),
                    KeyName = keyName
                };
            }

            InputSimulationWaitOutcome releaseOutcome =
                await ReleaseKeyStateIfPossible(keyboard, key).ConfigureAwait(false);

            if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleTimedOutHeldKeyCleanup(keyboard, key, keyName, false);
                return TimedOutKeyResult(UnityCliLoopKeyboardAction.KeyUp, keyName);
            }

            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);

            InputSimulationWaitOutcome waitOutcome = await InputSystemUpdateHelper.WaitForObservationFrames(ct)
                .ConfigureAwait(false);
            if (waitOutcome == InputSimulationWaitOutcome.Paused)
            {
                return InterruptedKeyResult(UnityCliLoopKeyboardAction.KeyUp, keyName);
            }

            if (waitOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                return TimedOutKeyResult(UnityCliLoopKeyboardAction.KeyUp, keyName);
            }

            return new UnityCliLoopKeyboardSimulationResult
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

        private static UnityCliLoopKeyboardSimulationResult InterruptedPressResult(string keyName)
        {
            return InterruptedKeyResult(UnityCliLoopKeyboardAction.Press, keyName);
        }

        private static UnityCliLoopKeyboardSimulationResult InterruptedKeyResult(
            UnityCliLoopKeyboardAction action,
            string keyName)
        {
            UnityCliLoopKeyboardSimulationResult result = new()
            {
                Success = true,
                Message = $"Keyboard input stopped because Unity paused during Debug Break inspection. Key '{keyName}' was released from Unity CLI Loop bookkeeping.",
                Action = action.ToString(),
                KeyName = keyName,
                InterruptedByDebugBreak = true
            };
            AttachDebugBreakHit(result);
            return result;
        }

        private static UnityCliLoopKeyboardSimulationResult TimedOutKeyResult(
            UnityCliLoopKeyboardAction action,
            string keyName)
        {
            return new UnityCliLoopKeyboardSimulationResult
            {
                Success = false,
                Message = $"Keyboard input timed out while waiting for Unity Editor update. Key '{keyName}' cleanup is queued for the next Editor tick.",
                Action = action.ToString(),
                KeyName = keyName
            };
        }

        private static void AttachDebugBreakHit(UnityCliLoopKeyboardSimulationResult result)
        {
            if (result == null)
            {
                Debug.Assert(false, "result must not be null");
                return;
            }

            UloopPausePointSnapshot? snapshot = UloopPausePointRegistry.GetLatestHitSnapshot();
            if (snapshot == null)
            {
                return;
            }

            if (!snapshot.IsHit)
            {
                return;
            }

            string? snapshotId = snapshot.Id;
            if (string.IsNullOrEmpty(snapshotId))
            {
                return;
            }

            result.DebugBreakId = snapshotId;
            result.DebugBreakHitCount = snapshot.HitCount;
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
            await EditorFrameWaiter.WaitFramesAsync(1, CancellationToken.None).ConfigureAwait(false);
        }

        private static async Task<InputSimulationWaitOutcome> RollbackHeldKey(Keyboard keyboard, Key key, string keyName)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
            InputSimulationWaitOutcome releaseOutcome =
                await ReleaseKeyStateIfPossible(keyboard, key).ConfigureAwait(false);
            if (releaseOutcome == InputSimulationWaitOutcome.TimedOut)
            {
                ScheduleTimedOutHeldKeyCleanup(keyboard, key, keyName, false);
                return releaseOutcome;
            }

            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
            return releaseOutcome;
        }

        private static async Task<InputSimulationWaitOutcome> ReleaseKeyStateIfPossible(Keyboard keyboard, Key key)
        {
            await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
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
                CancellationToken.None).ConfigureAwait(false);
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
                await ReleaseKeyStateIfPossible(keyboard, key).ConfigureAwait(false);
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
                await ReleaseKeyStateIfPossible(keyboard, key).ConfigureAwait(false);
            }

            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
        }

        private static bool CanInjectKeyboardState(Keyboard keyboard)
        {
            return EditorApplication.isPlaying && keyboard != null;
        }
#endif
    }
}
