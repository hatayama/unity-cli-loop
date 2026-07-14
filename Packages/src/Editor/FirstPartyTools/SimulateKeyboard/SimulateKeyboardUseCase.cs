#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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
                IReadOnlyList<string> suggestions = KeyboardKeyNameSuggester.Suggest(parameters.Key);
                string suggestionText = suggestions.Count == 0
                    ? string.Empty
                    : $" Did you mean: {string.Join(", ", suggestions)}?";
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message =
                        $"Invalid key name: \"{parameters.Key}\". Use Input System Key enum names (e.g. \"W\", \"Space\", \"LeftShift\", \"A\", \"Enter\").{suggestionText}",
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
                    response = await KeyboardInputActionExecutor.ExecutePress(
                        keyboard,
                        key,
                        parameters.Duration,
                        ct).ConfigureAwait(false);
                    break;

                case UnityCliLoopKeyboardAction.KeyDown:
                    response = await KeyboardInputActionExecutor.ExecuteKeyDown(keyboard, key, ct).ConfigureAwait(false);
                    break;

                case UnityCliLoopKeyboardAction.KeyUp:
                    response = await KeyboardInputActionExecutor.ExecuteKeyUp(keyboard, key, ct).ConfigureAwait(false);
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

        private static string NormalizeKeyName(string keyName)
        {
            if (string.Equals(keyName, "Return", StringComparison.OrdinalIgnoreCase))
            {
                return Key.Enter.ToString();
            }
            return keyName;
        }

#endif
    }
}
