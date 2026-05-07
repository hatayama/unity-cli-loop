using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bundled tool entry point that clears Unity Console entries through the public tool contract.
    /// </summary>
    [UnityCliLoopTool]
    public class ClearConsoleTool : UnityCliLoopTool<ClearConsoleSchema, ClearConsoleResponse>
    {
        public override string ToolName => "clear-console";

        protected override Task<ClearConsoleResponse> ExecuteAsync(ClearConsoleSchema parameters, CancellationToken ct)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            ct.ThrowIfCancellationRequested();
            ConsoleClearService consoleClear = new();
            UnityCliLoopConsoleClearResult result = consoleClear.Clear(parameters.AddConfirmationMessage);
            ClearConsoleResponse response = new(
                success: result.Success,
                clearedLogCount: result.ClearedLogCount,
                clearedCounts: new ClearedLogCounts(
                    result.ClearedCounts.ErrorCount,
                    result.ClearedCounts.WarningCount,
                    result.ClearedCounts.LogCount),
                message: result.Message);

            return Task.FromResult(response);
        }
    }
}
