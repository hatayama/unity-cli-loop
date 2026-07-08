using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for input replay.
    /// </summary>
    [UnityCliLoopTool]
    public class ReplayInputTool : UnityCliLoopTool<ReplayInputSchema, ReplayInputResponse>
    {
        public override string ToolName => "replay-input";

        protected override async Task<ReplayInputResponse> ExecuteAsync(ReplayInputSchema parameters, CancellationToken ct)
        {
            ReplayInputUseCase useCase = new();
            return await useCase.ReplayInputAsync(parameters, ct);
        }
    }
}
