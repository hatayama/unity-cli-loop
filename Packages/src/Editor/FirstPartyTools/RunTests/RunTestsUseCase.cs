using System;
using System.Threading.Tasks;
using System.Threading;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Handles temporal cohesion for test execution processing
    /// Processing sequence: 1. Test filter creation, 2. Test execution, 3. Result processing
    /// Related classes: RunTestsTool, TestFilterCreationService, TestExecutionService
    /// </summary>
    public class RunTestsUseCase
    {
        private readonly TestFilterCreationService _filterService;
        private readonly TestExecutionService _executionService;
        private readonly TestExecutionStateValidationService _validationService;
        private readonly RunTestsNoTestsDiagnosticService _noTestsDiagnosticService;
        private readonly Func<string[]> _clearActivePausePoints;
        private readonly Func<CancellationToken, Task> _waitForTestRunnerCleanupAsync;
        private const int TestRunnerCleanupFallbackDelayMilliseconds = 3000;

        public RunTestsUseCase()
            : this(
                new TestFilterCreationService(),
                new TestExecutionService(),
                new TestExecutionStateValidationService())
        {
        }

        public RunTestsUseCase(
            TestFilterCreationService filterService,
            TestExecutionService executionService,
            TestExecutionStateValidationService validationService,
            Func<string[]> clearActivePausePoints = null,
            Func<CancellationToken, Task> waitForTestRunnerCleanupAsync = null)
        {
            Debug.Assert(filterService != null, "filterService must not be null");
            Debug.Assert(executionService != null, "executionService must not be null");
            Debug.Assert(validationService != null, "validationService must not be null");
            _filterService = filterService;
            _executionService = executionService;
            _validationService = validationService;
            _noTestsDiagnosticService = new RunTestsNoTestsDiagnosticService();
            _clearActivePausePoints = clearActivePausePoints ?? ClearActivePausePointsDefault;
            _waitForTestRunnerCleanupAsync = waitForTestRunnerCleanupAsync ?? WaitForTestRunnerCleanupAsync;
        }

        /// <summary>
        /// Executes test execution processing
        /// </summary>
        /// <param name="parameters">Test execution parameters</param>
        /// <param name="ct">Cancellation control token</param>
        /// <returns>Test execution result</returns>
        public async Task<RunTestsResponse> ExecuteAsync(RunTestsSchema parameters, CancellationToken ct)
        {
            if (parameters == null)
            {
                throw new System.ArgumentNullException(nameof(parameters));
            }

            if (!IsSupportedTestMode(parameters.TestMode))
            {
                return CreateFailureResponse("Unsupported test mode: " + parameters.TestMode);
            }

            ct.ThrowIfCancellationRequested();
            if (!_executionService.IsTestFrameworkAvailable)
            {
                return RunTestsResponse.CreateTestFrameworkUnavailable();
            }

            (bool isTimeoutValid, string timeoutError) = RunTestsExecutionTimeout.Validate(parameters.TimeoutSeconds);
            if (!isTimeoutValid)
            {
                return CreateFailureResponse(timeoutError);
            }

            ValidationResult validation = _validationService.Validate(parameters.TestMode, parameters.SaveBeforeRun);
            if (!validation.IsValid)
            {
                return CreateFailureResponse(validation.ErrorMessage);
            }

            // 1. Test filter creation
            TestExecutionFilter filter = null;
            if (parameters.FilterType != TestFilterType.all)
            {
                (TestExecutionFilter createdFilter, string filterError) = _filterService.TryCreateFilter(parameters.FilterType, parameters.FilterValue);
                if (filterError != null)
                {
                    return CreateFailureResponse(filterError);
                }
                filter = createdFilter;
            }

            string[] clearedPausePointIds = _clearActivePausePoints();

            // 2. Test execution
            // Why these awaits do not use ConfigureAwait(false): the entry clears all active
            // pause points and the server is single-flight (BUSY rejects concurrent commands),
            // so no CLI-originated pause can fire during test execution. The only remaining
            // pause source is a human manually pausing via the Editor UI, which is native
            // Unity behavior and out of scope.
            ct.ThrowIfCancellationRequested();
            using CancellationTokenSource timeoutCancellationTokenSource =
                RunTestsExecutionTimeout.CreateLinkedTimeoutSource(ct, parameters.TimeoutSeconds);
            CancellationToken executionCt = timeoutCancellationTokenSource.Token;
            SerializableTestResult result;
            try
            {
                if (parameters.TestMode == UnityCliLoopTestMode.PlayMode)
                {
                    result = await _executionService.ExecutePlayModeTestAsync(filter, executionCt);
                }
                else
                {
                    result = await _executionService.ExecuteEditModeTestAsync(filter, executionCt);
                }

                // Why parent ct (not executionCt): CancelAfter only guards the RunFinished wait.
                // Using the linked token here would mis-report a successful run as timed out when
                // the fixed cleanup delay straddles the deadline after RunFinished already arrived.
                await _waitForTestRunnerCleanupAsync(ct);
            }
            catch (RunTestsExecutionCanceledException canceledException)
                when (RunTestsExecutionTimeout.IsTimeoutCancellation(ct))
            {
                // Why return a tool failure instead of rethrowing: agents need an actionable
                // timeout message (extend --timeout-seconds / launch -r). Parent/disconnect
                // cancellation still propagates so the IPC session can tear down normally.
                return CreateFailureResponse(
                    RunTestsExecutionTimeout.CreateTimeoutMessage(
                        parameters.TimeoutSeconds,
                        canceledException.StopResult.DegradationNote));
            }
            catch (OperationCanceledException) when (RunTestsExecutionTimeout.IsTimeoutCancellation(ct))
            {
                return CreateFailureResponse(
                    RunTestsExecutionTimeout.CreateTimeoutMessage(parameters.TimeoutSeconds));
            }

            // 3. Response creation.
            RunTestsResponse response = new(
                success: result.success,
                message: result.message,
                completedAt: result.completedAt,
                testCount: result.testCount,
                passedCount: result.passedCount,
                failedCount: result.failedCount,
                skippedCount: result.skippedCount,
                xmlPath: result.xmlPath,
                status: result.status,
                hasFailures: result.hasFailures,
                noTestsFound: result.noTestsFound,
                noTestsFoundExplanation: result.noTestsFoundExplanation);
            if (clearedPausePointIds != null && clearedPausePointIds.Length > 0)
            {
                response.ClearedPausePointIds = clearedPausePointIds;
            }
            response.Message = RunTestsNoTestsDiagnosticService.AppendDiagnosticsOrOriginalMessage(
                response.Message,
                () => _noTestsDiagnosticService.AppendDiagnosticsIfNeeded(
                    response.Message,
                    response.NoTestsFound,
                    parameters.TestMode,
                    parameters.FilterType));
            return response;
        }

        private static bool IsSupportedTestMode(UnityCliLoopTestMode testMode)
        {
            return Enum.IsDefined(typeof(UnityCliLoopTestMode), testMode);
        }

        private static RunTestsResponse CreateFailureResponse(string message)
        {
            return new RunTestsResponse(
                success: false,
                message: message,
                completedAt: DateTime.UtcNow.ToString("o"),
                testCount: 0,
                passedCount: 0,
                failedCount: 0,
                skippedCount: 0,
                xmlPath: null,
                status: RunTestsExecutionStatus.ExecutionFailed,
                hasFailures: false,
                noTestsFound: false,
                noTestsFoundExplanation: string.Empty);
        }

        private static string[] ClearActivePausePointsDefault()
        {
            UloopPausePointClearAllResult result = UloopPausePointRegistry.ClearAll(UloopPausePointClearedReason.RunTestsAutoClear);
            if (result.ClearedCount == 0)
            {
                return null;
            }
            return result.ClearedIds;
        }

        private static async Task WaitForTestRunnerCleanupAsync(CancellationToken ct)
        {
            // Why: Unity Test Framework exposes the real active-run signal only through internal API,
            // while the public RunFinished callback fires before cleanup tasks such as RestoreSceneSetupTask.
            await TimerDelay.Wait(TestRunnerCleanupFallbackDelayMilliseconds, ct);
        }
    }
}
