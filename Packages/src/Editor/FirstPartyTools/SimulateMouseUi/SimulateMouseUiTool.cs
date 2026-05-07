using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for EventSystem mouse simulation.
    /// </summary>
    [UnityCliLoopTool]
    public class SimulateMouseUiTool : UnityCliLoopTool<SimulateMouseUiSchema, SimulateMouseUiResponse>
    {
        public override string ToolName => "simulate-mouse-ui";

        protected override async Task<SimulateMouseUiResponse> ExecuteAsync(SimulateMouseUiSchema parameters, CancellationToken ct)
        {
            SimulateMouseUiUseCase useCase = new();
            UnityCliLoopMouseUiSimulationResult result =
                await useCase.SimulateMouseUiAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopMouseUiSimulationRequest ToRequest(SimulateMouseUiSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopMouseUiSimulationRequest
            {
                Action = parameters.Action,
                X = parameters.X,
                Y = parameters.Y,
                FromX = parameters.FromX,
                FromY = parameters.FromY,
                DragSpeed = parameters.DragSpeed,
                Duration = parameters.Duration,
                Button = parameters.Button,
                BypassRaycast = parameters.BypassRaycast,
                TargetPath = parameters.TargetPath,
                DropTargetPath = parameters.DropTargetPath,
            };
        }

        private static SimulateMouseUiResponse ToResponse(UnityCliLoopMouseUiSimulationResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            return new SimulateMouseUiResponse
            {
                Success = result.Success,
                Message = result.Message,
                Action = result.Action,
                HitGameObjectName = result.HitGameObjectName,
                PositionX = result.PositionX,
                PositionY = result.PositionY,
                EndPositionX = result.EndPositionX,
                EndPositionY = result.EndPositionY,
            };
        }
    }
}
