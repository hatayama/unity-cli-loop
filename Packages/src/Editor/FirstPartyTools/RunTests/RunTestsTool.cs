using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point for Unity Test Runner execution.
    /// </summary>
    [UnityCliLoopTool]
    public class RunTestsTool : UnityCliLoopTool<RunTestsSchema, RunTestsResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_RUN_TESTS;

        protected override async Task<RunTestsResponse> ExecuteAsync(RunTestsSchema parameters, CancellationToken ct)
        {
            RunTestsUseCase useCase = new();
            return await useCase.ExecuteAsync(parameters, ct).ConfigureAwait(false);
        }
    }
}
