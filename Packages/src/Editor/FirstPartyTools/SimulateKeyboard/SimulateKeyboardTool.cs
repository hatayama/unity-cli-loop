using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for keyboard simulation.
    /// </summary>
    [UnityCliLoopTool]
    public class SimulateKeyboardTool : UnityCliLoopTool<SimulateKeyboardSchema, SimulateKeyboardResponse>
    {
        public override string ToolName => "simulate-keyboard";

        protected override async Task<SimulateKeyboardResponse> ExecuteAsync(SimulateKeyboardSchema parameters, CancellationToken ct)
        {
            SimulateKeyboardUseCase useCase = new();
            UnityCliLoopKeyboardSimulationResult result = await useCase.SimulateKeyboardAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopKeyboardSimulationRequest ToRequest(SimulateKeyboardSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopKeyboardSimulationRequest
            {
                Action = parameters.Action,
                Key = parameters.Key,
                Duration = parameters.Duration,
            };
        }

        private static SimulateKeyboardResponse ToResponse(UnityCliLoopKeyboardSimulationResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            return new SimulateKeyboardResponse
            {
                Success = result.Success,
                Message = result.Message,
                Action = result.Action,
                KeyName = result.KeyName,
            };
        }
    }
}
