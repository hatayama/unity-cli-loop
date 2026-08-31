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
            return await useCase.ExecuteAsync(parameters, ct).ConfigureAwait(false);
        }
    }
}
