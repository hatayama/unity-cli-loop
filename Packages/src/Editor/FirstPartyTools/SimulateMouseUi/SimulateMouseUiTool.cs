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
            return await useCase.ExecuteAsync(parameters, ct);
        }
    }
}
