#nullable enable
using System;
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
    /// Coordinates Input System mouse simulation for the bundled simulate-mouse-input tool.
    /// </summary>
    public class SimulateMouseInputUseCase
    {
        // Wire-visible fragment of the paused preflight message; tests pin the composed string.
        public const string PausedActionDescription = "simulating mouse input";

#if !ULOOP_HAS_INPUT_SYSTEM
#pragma warning disable CS1998
#endif
        public async Task<SimulateMouseInputResponse> ExecuteAsync(
            SimulateMouseInputSchema parameters,
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
            return new SimulateMouseInputResponse
            {
                Success = false,
                Message = InputSystemPackageRequirementMessage.Format("simulate-mouse-input"),
                Action = parameters.Action.ToString()
            };
#else
            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            ValidationResult preflight = PlayModeToolPreflightService.RequireActiveAndNotPaused(PausedActionDescription);
            if (!preflight.IsValid)
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = preflight.ErrorMessage,
                    Action = parameters.Action.ToString()
                };
            }

            if (!System.Enum.IsDefined(typeof(UnityCliLoopMouseInputAction), parameters.Action))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Invalid Action value: {(int)parameters.Action}. Use Click, LongPress, MoveDelta, Scroll, or SmoothDelta.",
                    Action = parameters.Action.ToString()
                };
            }

            if (!System.Enum.IsDefined(typeof(UnityCliLoopMouseButton), parameters.Button))
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = $"Invalid Button value: {(int)parameters.Button}. Use Left, Right, or Middle.",
                    Action = parameters.Action.ToString()
                };
            }

            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return new SimulateMouseInputResponse
                {
                    Success = false,
                    Message = "No mouse device found in Input System. Ensure the Input System package is properly configured.",
                    Action = parameters.Action.ToString()
                };
            }

            UloopPausePointRegistry.ClearLatestHitSnapshot();

            VibeLogger.LogInfo(
                "simulate_mouse_input_start",
                "Mouse input simulation started",
                new { Action = parameters.Action.ToString(), Button = parameters.Button.ToString() },
                correlationId: correlationId
            );

            using InputSimulationRunInBackgroundScope runInBackgroundScope = InputSimulationRunInBackgroundScope.Enable();

            EnsureOverlayExists();

            SimulateMouseInputResponse response;

            switch (parameters.Action)
            {
                case UnityCliLoopMouseInputAction.Click:
                    response = await MouseInputPressActionExecutor.ExecuteClick(mouse, parameters, ct).ConfigureAwait(false);
                    break;

                case UnityCliLoopMouseInputAction.LongPress:
                    response = await MouseInputPressActionExecutor.ExecuteLongPress(mouse, parameters, ct).ConfigureAwait(false);
                    break;

                case UnityCliLoopMouseInputAction.MoveDelta:
                    response = await MouseInputMotionActionExecutor.ExecuteMoveDelta(mouse, parameters, ct).ConfigureAwait(false);
                    break;

                case UnityCliLoopMouseInputAction.Scroll:
                    response = await MouseInputMotionActionExecutor.ExecuteScroll(mouse, parameters, ct).ConfigureAwait(false);
                    break;

                case UnityCliLoopMouseInputAction.SmoothDelta:
                    response = await MouseInputMotionActionExecutor.ExecuteSmoothDelta(mouse, parameters, ct).ConfigureAwait(false);
                    break;

                default:
                    throw new ArgumentException($"Unknown mouse input action: {parameters.Action}");
            }

            VibeLogger.LogInfo(
                "simulate_mouse_input_complete",
                $"Mouse input simulation completed: {response.Message}",
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

#endif
    }
}
