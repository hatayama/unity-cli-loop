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
            return await useCase.ExecuteAsync(parameters, ct);
        }
    }
}
