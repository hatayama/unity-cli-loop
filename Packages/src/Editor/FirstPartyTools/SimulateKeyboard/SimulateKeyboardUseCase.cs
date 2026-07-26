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

            // ReleaseAll must work while paused so agents can recover stuck device state after a
            // pause-point interruption without first resuming PlayMode.
            PlayModeToolPreflightResult preflight = parameters.Action == UnityCliLoopKeyboardAction.ReleaseAll
                ? PlayModeToolPreflightService.RequireActive()
                : PlayModeToolPreflightService.RequireActiveAndNotPaused(PausedActionDescription);
            if (!preflight.IsValid)
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = preflight.ErrorMessage,
                    Action = parameters.Action.ToString(),
                    RejectedByActivePausePointId = preflight.RejectedByActivePausePointId
                };
            }

            if (parameters.Action == UnityCliLoopKeyboardAction.ReleaseAll)
            {
                return await ExecuteReleaseAllAsync(correlationId, ct).ConfigureAwait(false);
            }

            if (string.IsNullOrEmpty(parameters.Key))
            {
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message = "Key parameter is required. Examples: \"W\", \"Space\", \"LeftShift\", \"A\", \"Enter\", \"Digit3\".",
                    Action = parameters.Action.ToString()
                };
            }

            // Why not Enum.TryParse alone: it also accepts ordinals ("3"), signed ordinals ("+3"),
            // whitespace-padded input, comma-separated names OR-ed together ("Space,Enter"), and
            // undefined ordinals ("300") that later throw from the keyboard indexer. Only a name
            // that is defined on the Key enum may resolve to a key.
            string normalizedKey = NormalizeKeyName(parameters.Key);
            if (!DefinedKeysByName.TryGetValue(normalizedKey, out Key key) || key == Key.None)
            {
                // Suggest from the normalized form so padding does not degrade the candidates,
                // while the message below still reports the raw input verbatim.
                IReadOnlyList<string> suggestions = KeyboardKeyNameSuggester.Suggest(normalizedKey);
                string suggestionText = suggestions.Count == 0
                    ? string.Empty
                    : $" Did you mean: {string.Join(", ", suggestions)}?";
                // Why: digits used to resolve silently to unrelated keys, so earlier runs that
                // reported success may have pressed something else and need to be re-checked.
                string ordinalHistoryText = LooksLikeNumericKeyInput(parameters.Key)
                    ? " Digits are not key names: bare digits were previously parsed as enum ordinals (e.g. \"3\" pressed Tab), so re-check any earlier results or scripts that passed digits."
                    : string.Empty;
                return new SimulateKeyboardResponse
                {
                    Success = false,
                    Message =
                        $"Invalid key name: \"{parameters.Key}\". Use Input System Key enum names (e.g. \"W\", \"Space\", \"LeftShift\", \"A\", \"Enter\", \"Digit3\").{suggestionText}{ordinalHistoryText}",
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

            // Why not `using`: the executor awaits below use ConfigureAwait(false), so this method
            // can resume on a thread-pool thread. Application.runInBackground is main-thread-only,
            // so the scope must be disposed after switching back to the main thread.
            InputSimulationRunInBackgroundScope runInBackgroundScope = InputSimulationRunInBackgroundScope.Enable();
            try
            {
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
            }
            finally
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                runInBackgroundScope.Dispose();
            }
#endif
        }

#if ULOOP_HAS_INPUT_SYSTEM
        private static async Task<SimulateKeyboardResponse> ExecuteReleaseAllAsync(
            string correlationId,
            CancellationToken ct)
        {
            // Why not ClearLatestHitSnapshot: ReleaseAll is intended for recovery while a
            // pause-point inspection is still active; clearing would drop TryGetCapturedValue
            // live references that agents need during that pause.

            VibeLogger.LogInfo(
                "simulate_keyboard_start",
                "Keyboard simulation started",
                new { Action = UnityCliLoopKeyboardAction.ReleaseAll.ToString() },
                correlationId: correlationId
            );

            // Why not `using`: SwitchToMainThreadIfNeeded can resume on a thread-pool thread
            // after ConfigureAwait(false), and Application.runInBackground is main-thread-only.
            InputSimulationRunInBackgroundScope runInBackgroundScope = InputSimulationRunInBackgroundScope.Enable();
            try
            {
                EnsureOverlayExists();
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(ct);
                Keyboard keyboard = Keyboard.current;
                IReadOnlyList<string> releasedKeys =
                    KeyboardInputMainThreadCleanup.ReleaseAllKeysImmediately(keyboard);
                List<string> releasedKeysList = new List<string>(releasedKeys);

                string message = releasedKeysList.Count == 0
                    ? "Released all keys (none were held)."
                    : $"Released {releasedKeysList.Count} key(s): {string.Join(", ", releasedKeysList)}";

                SimulateKeyboardResponse response = new SimulateKeyboardResponse
                {
                    Success = true,
                    Message = message,
                    Action = UnityCliLoopKeyboardAction.ReleaseAll.ToString(),
                    ReleasedKeys = releasedKeysList
                };

                VibeLogger.LogInfo(
                    "simulate_keyboard_complete",
                    $"Keyboard simulation completed: {response.Message}",
                    new { Action = UnityCliLoopKeyboardAction.ReleaseAll.ToString(), Success = true },
                    correlationId: correlationId
                );

                return response;
            }
            finally
            {
                await InputSystemUpdateHelper.SwitchToMainThreadIfNeeded(CancellationToken.None);
                runInBackgroundScope.Dispose();
            }
        }

        private static void EnsureOverlayExists()
        {
            OverlayCanvasFactory.EnsureExists();
        }

        // Immutable name-to-value map of the Input System Key enum, so key resolution never falls
        // back to Enum.TryParse's ordinal and flag-combination behavior.
        private static readonly IReadOnlyDictionary<string, Key> DefinedKeysByName = BuildDefinedKeysByName();

        private static IReadOnlyDictionary<string, Key> BuildDefinedKeysByName()
        {
            string[] names = Enum.GetNames(typeof(Key));
            Array values = Enum.GetValues(typeof(Key));
            Dictionary<string, Key> keysByName = new(names.Length, StringComparer.OrdinalIgnoreCase);
            for (int index = 0; index < names.Length; index++)
            {
                keysByName[names[index]] = (Key)values.GetValue(index);
            }

            return keysByName;
        }

        /// <summary>
        /// Reports whether the raw key input is the numeric form that Enum.TryParse used to accept
        /// as an enum ordinal, so the rejection can explain what earlier runs actually pressed.
        /// </summary>
        private static bool LooksLikeNumericKeyInput(string keyName)
        {
            string trimmed = keyName.Trim();
            if (trimmed.Length > 0 && (trimmed[0] == '+' || trimmed[0] == '-'))
            {
                trimmed = trimmed.Substring(1);
            }

            if (trimmed.Length == 0)
            {
                return false;
            }

            for (int index = 0; index < trimmed.Length; index++)
            {
                // Why not char.IsDigit: it is true for non-ASCII digits, which Enum.TryParse never
                // accepted as ordinals. Claiming they used to press another key would be false.
                if (trimmed[index] < '0' || trimmed[index] > '9')
                {
                    return false;
                }
            }

            return true;
        }

        private static string NormalizeKeyName(string keyName)
        {
            // Why trim here rather than at the whitelist comparison: Enum.TryParse used to accept
            // whitespace-padded names, so padded correct input already worked. Blocking ambiguous
            // input must not narrow correct input, and the alias has to see the padded form too.
            string trimmed = keyName.Trim();
            if (string.Equals(trimmed, "Return", StringComparison.OrdinalIgnoreCase))
            {
                return Key.Enter.ToString();
            }
            return trimmed;
        }

#endif
    }
}
