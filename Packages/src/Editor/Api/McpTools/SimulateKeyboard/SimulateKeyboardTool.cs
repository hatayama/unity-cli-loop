#nullable enable
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
#if ULOOP_HAS_INPUT_SYSTEM
using UnityEngine.InputSystem;
#endif

namespace io.github.hatayama.UnityCliLoop
{
    [McpTool(Description = "Simulate keyboard key input in PlayMode via Input System. Supports one-shot press, key-down hold, and key-up release for game controls (WASD, Space, etc.). Requires the Input System package (com.unity.inputsystem).")]
    public class SimulateKeyboardTool : AbstractUnityTool<SimulateKeyboardSchema, SimulateKeyboardResponse>
    {
        public override string ToolName => "simulate-keyboard";

        protected override
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
            async Task<SimulateKeyboardResponse> ExecuteAsync(
            SimulateKeyboardSchema parameters,
            CancellationToken ct)
#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning restore CS1998
#endif
        {
            ct.ThrowIfCancellationRequested();

#if !ULOOP_HAS_INPUT_SYSTEM
            return new SimulateKeyboardResponse
            {
                Success = false,
                Message = "simulate-keyboard requires the Input System package (com.unity.inputsystem). Install it via Package Manager and set Active Input Handling to 'Input System Package (New)' or 'Both' in Player Settings.",
                Action = parameters.Action.ToString()
            };
#else
            string correlationId = McpConstants.GenerateCorrelationId();

            if (!EditorApplication.isPlaying)
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = "PlayMode is not active. Use control-play-mode tool to start PlayMode first.",
                    Action = parameters.Action.ToString()
                };
            }

            if (EditorApplication.isPaused)
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = "PlayMode is paused. Resume PlayMode before simulating keyboard input.",
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

            VibeLogger.LogInfo(
                "simulate_keyboard_start",
                "Keyboard simulation started",
                new { Action = parameters.Action.ToString(), Key = parameters.Key },
                correlationId: correlationId
            );

            EnsureOverlayExists();

            SimulateKeyboardResponse response;

            switch (parameters.Action)
            {
                case KeyboardAction.Press:
                    response = await ExecutePress(keyboard, key, parameters.Duration, ct);
                    break;

                case KeyboardAction.KeyDown:
                    response = await ExecuteKeyDown(keyboard, key, ct);
                    break;

                case KeyboardAction.KeyUp:
                    response = await ExecuteKeyUp(keyboard, key, ct);
                    break;

                default:
                    throw new ArgumentException($"Unknown keyboard action: {parameters.Action}");
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
                    Action = KeyboardAction.Press.ToString(),
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
                    Action = KeyboardAction.Press.ToString(),
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
            return new SimulateKeyboardResponse
            {
                Success = true,
                Message = $"Pressed '{keyName}'{durationText}",
                Action = KeyboardAction.Press.ToString(),
                KeyName = keyName
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
                    Action = KeyboardAction.KeyDown.ToString(),
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

            return new SimulateKeyboardResponse
            {
                Success = true,
                Message = $"Key '{keyName}' held down",
                Action = KeyboardAction.KeyDown.ToString(),
                KeyName = keyName
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
                    Action = KeyboardAction.KeyUp.ToString(),
                    KeyName = keyName
                };
            }

            await InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(() => KeyboardKeyState.SetKeyState(keyboard, key, false), ct);
            KeyboardKeyState.SetKeyUp(key);
            SimulateKeyboardOverlayState.RemoveHeldKey(keyName);
            await InputSystemUpdateHelper.WaitForObservationFrames(ct);

            return new SimulateKeyboardResponse
            {
                Success = true,
                Message = $"Key '{keyName}' released",
                Action = KeyboardAction.KeyUp.ToString(),
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
