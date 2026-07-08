using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for input recording.
    /// </summary>
    [UnityCliLoopTool]
    public class RecordInputTool : UnityCliLoopTool<RecordInputSchema, RecordInputResponse>
    {
        public override string ToolName => "record-input";

        protected override async Task<RecordInputResponse> ExecuteAsync(RecordInputSchema parameters, CancellationToken ct)
        {
            RecordInputUseCase useCase = new();
            return await useCase.RecordInputAsync(parameters, ct).ConfigureAwait(false);
        }
    }
}
