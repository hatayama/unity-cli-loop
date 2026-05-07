using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for mouse input simulation.
    /// </summary>
    [UnityCliLoopTool]
    public class SimulateMouseInputTool : UnityCliLoopTool<SimulateMouseInputSchema, SimulateMouseInputResponse>
    {
        public override string ToolName => "simulate-mouse-input";

        protected override async Task<SimulateMouseInputResponse> ExecuteAsync(SimulateMouseInputSchema parameters, CancellationToken ct)
        {
            SimulateMouseInputUseCase useCase = new();
            UnityCliLoopMouseInputSimulationResult result =
                await useCase.SimulateMouseInputAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopMouseInputSimulationRequest ToRequest(SimulateMouseInputSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopMouseInputSimulationRequest
            {
                Action = parameters.Action,
                X = parameters.X,
                Y = parameters.Y,
                Button = parameters.Button,
                Duration = parameters.Duration,
                DeltaX = parameters.DeltaX,
                DeltaY = parameters.DeltaY,
                ScrollX = parameters.ScrollX,
                ScrollY = parameters.ScrollY,
            };
        }

        private static SimulateMouseInputResponse ToResponse(UnityCliLoopMouseInputSimulationResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            return new SimulateMouseInputResponse
            {
                Success = result.Success,
                Message = result.Message,
                Action = result.Action,
                Button = result.Button,
                PositionX = result.PositionX,
                PositionY = result.PositionY,
            };
        }
    }
}
