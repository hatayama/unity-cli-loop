using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point that exposes Unity Console snapshots through the public tool contract.
    /// </summary>
    [UnityCliLoopTool]
    public class GetLogsTool : UnityCliLoopTool<GetLogsSchema, GetLogsResponse>
    {
        public override string ToolName => "get-logs";

        protected override async Task<GetLogsResponse> ExecuteAsync(GetLogsSchema parameters, CancellationToken ct)
        {
            GetLogsUseCase useCase = new(new LogRetrievalService(), new LogFilteringService());
            return await useCase.ExecuteAsync(parameters, ct);
        }
    }
}
