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
        public override string ToolName => "run-tests";

        protected override async Task<RunTestsResponse> ExecuteAsync(RunTestsSchema parameters, CancellationToken ct)
        {
            RunTestsUseCase useCase = new();
            UnityCliLoopTestExecutionResult result = await useCase.RunTestsAsync(ToRequest(parameters), ct);
            return ToResponse(result);
        }

        private static UnityCliLoopTestExecutionRequest ToRequest(RunTestsSchema parameters)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            return new UnityCliLoopTestExecutionRequest
            {
                TestMode = ToContractTestMode(parameters.TestMode),
                FilterType = parameters.FilterType,
                FilterValue = parameters.FilterValue,
                SaveBeforeRun = parameters.SaveBeforeRun,
            };
        }

        private static UnityCliLoopTestMode ToContractTestMode(UnityEditor.TestTools.TestRunner.Api.TestMode testMode)
        {
            if (testMode == UnityEditor.TestTools.TestRunner.Api.TestMode.PlayMode)
            {
                return UnityCliLoopTestMode.PlayMode;
            }

            return UnityCliLoopTestMode.EditMode;
        }

        private static RunTestsResponse ToResponse(UnityCliLoopTestExecutionResult result)
        {
            if (result == null)
            {
                throw new System.ArgumentNullException(nameof(result));
            }

            return new RunTestsResponse(
                success: result.Success,
                message: result.Message,
                completedAt: result.CompletedAt,
                testCount: result.TestCount,
                passedCount: result.PassedCount,
                failedCount: result.FailedCount,
                skippedCount: result.SkippedCount,
                xmlPath: result.XmlPath);
        }
    }
}
