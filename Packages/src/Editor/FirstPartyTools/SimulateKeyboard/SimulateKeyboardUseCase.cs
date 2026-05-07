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

            VibeLogger.LogInfo(
                "simulate_keyboard_start",
                "Keyboard simulation started",
                new { Action = request.Action.ToString(), Key = request.Key },
                correlationId: correlationId
            );

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

            try
            {
                await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(() => KeyboardKeyState.SetKeyState(keyboard, key, true), ct);
                pressWasApplied = true;
                await InputSystemUpdateHelper.WaitForPressLifetime(duration, ct);
            }
            finally
            {
                if (pressWasApplied)
                {
                    await ReleaseKeyStateIfPossible(keyboard, key);
                    KeyboardKeyState.UnregisterTransientKey(key);
                    await FinalizePressOverlay(ct);
                }
                else
                {
                    KeyboardKeyState.UnregisterTransientKey(key);
                    SimulateKeyboardOverlayState.ClearPress();
                }
            }

            string durationText = duration > 0f ? $" for {duration:F1}s" : "";
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

            try
            {
                await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(() => KeyboardKeyState.SetKeyState(keyboard, key, true), ct);
                keyDownApplied = true;
                KeyboardKeyState.SetKeyDown(key);
                SimulateKeyboardOverlayState.AddHeldKey(keyName);
                await InputSystemUpdateHelper.WaitForObservationFrames(ct);
                committed = true;
            }
            finally
            {
                if (keyDownApplied && !committed)
                {
                    await RollbackHeldKey(keyboard, key, keyName);
                }
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

            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(() => KeyboardKeyState.SetKeyState(keyboard, key, false), ct);
            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
            await InputSystemUpdateHelper.WaitForObservationFrames(ct);

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

        private static async Task FinalizePressOverlay(CancellationToken ct)
        {
            if (ct.IsCancellationRequested)
            {
                SimulateKeyboardOverlayState.ClearPress();
                return;
            }

            SimulateKeyboardOverlayState.ReleasePress();
            await EditorDelay.DelayFrame(1, CancellationToken.None);
        }

        private static async Task RollbackHeldKey(Keyboard keyboard, Key key, string keyName)
        {
            await ReleaseKeyStateIfPossible(keyboard, key);
            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
        }

        private static async Task ReleaseKeyStateIfPossible(Keyboard keyboard, Key key)
        {
            if (!CanInjectKeyboardState(keyboard))
            {
                return;
            }

            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(() => KeyboardKeyState.SetKeyState(keyboard, key, false), CancellationToken.None);
        }

        private static bool CanInjectKeyboardState(Keyboard keyboard)
        {
            return EditorApplication.isPlaying && !EditorApplication.isPaused && Keyboard.current == keyboard;
        }
#endif
    }
}
