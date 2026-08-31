#nullable enable
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Coordinates EventSystem mouse simulation for the bundled simulate-mouse-ui tool.
    /// </summary>
    public class SimulateMouseUiUseCase
    {
        // Wire-visible fragment of the paused preflight message; tests pin the composed string.
        public const string PausedActionDescription = "simulating UI input";

        private readonly MouseUiMainThreadCleanupScheduler _mainThreadCleanupScheduler = new();

        public async Task<SimulateMouseUiResponse> ExecuteAsync(
            SimulateMouseUiSchema request,
            CancellationToken ct)
        {
            if (request == null)
            {
                throw new System.ArgumentNullException(nameof(request));
            }

            ct.ThrowIfCancellationRequested();
            _mainThreadCleanupScheduler.CaptureMainThreadContext();

            (MouseUiSimulationCommand? parameters, string? actionError) = MouseUiSimulationCommand.TryFromSchema(request);
            if (parameters == null)
            {
                // TryFromSchema guarantees actionError is non-null when command is null.
                return new SimulateMouseUiResponse
                {
                    Success = false,
                    Message = actionError!,
                    Action = request.Action.ToString()
                };
            }

            string correlationId = UnityCliLoopConstants.GenerateCorrelationId();

            EventSystem? eventSystem = EventSystem.current;
            SimulateMouseUiResponse? validationFailure = MouseUiSimulationValidator.ValidateSimulationStart(
                parameters,
                eventSystem,
                PausedActionDescription);
            if (validationFailure != null)
            {
                return validationFailure;
            }
            Debug.Assert(eventSystem != null, "ValidateSimulationStart must reject a missing EventSystem.");
            EventSystem activeEventSystem = eventSystem!;

            MouseUiSimulationActivityLogger.LogSimulationStart(parameters, correlationId);
            OverlayCanvasFactory.EnsureExists();

            SimulateMouseUiResponse? dragStateFailure =
                MouseUiSimulationValidator.ValidateActiveDragState(parameters);
            if (dragStateFailure != null)
            {
                return dragStateFailure;
            }

            SimulateMouseUiResponse response =
                await ExecuteMouseAction(parameters, activeEventSystem, ct).ConfigureAwait(false);
            MouseUiSimulationActivityLogger.LogSimulationComplete(parameters, response, correlationId);

            return response;
        }

        private async Task<SimulateMouseUiResponse> ExecuteMouseAction(
            MouseUiSimulationCommand parameters,
            EventSystem eventSystem,
            CancellationToken ct)
        {
            switch (parameters.Action)
            {
                case MouseAction.Click:
                    return await MouseUiPressActionExecutor.ExecuteClick(
                        parameters,
                        eventSystem,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                case MouseAction.Drag:
                    return await MouseUiOneShotDragExecutor.ExecuteDragOneShot(
                        parameters,
                        eventSystem,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                case MouseAction.DragStart:
                    return await MouseUiIncrementalDragExecutor.ExecuteDragStart(
                        parameters,
                        eventSystem,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                case MouseAction.DragMove:
                    return await MouseUiIncrementalDragExecutor.ExecuteDragMove(
                        parameters,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                case MouseAction.DragEnd:
                    return await MouseUiIncrementalDragExecutor.ExecuteDragEnd(
                        parameters,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                case MouseAction.LongPress:
                    return await MouseUiPressActionExecutor.ExecuteLongPress(
                        parameters,
                        eventSystem,
                        _mainThreadCleanupScheduler,
                        ct).ConfigureAwait(false);

                default:
                    // Unreachable when TryFromSchema succeeds; kept as a defensive Success=false response
                    // instead of a throw so any future MouseAction addition surfaces as a validation failure.
                    return MouseUiSimulationResponseFactory.CreateFailure(
                        parameters,
                        $"Unknown mouse action: {parameters.Action}");
            }
        }

    }
}
