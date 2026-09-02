using System;
using System.Globalization;
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
        private readonly Func<string, bool, UnityCliLoopTestMode, TestFilterType, string> _appendNoTestsDiagnostics;
        private readonly Func<string[]> _clearActivePausePoints;
        private readonly Func<CancellationToken, Task> _waitForTestRunnerCleanupAsync;
        private readonly Func<int> _getActiveHotReloadChangeCount;
        private readonly Func<UnityCliLoopTestMode, RunTestsTestAsmdefProposal> _proposeTestAsmdef;
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
            Func<CancellationToken, Task> waitForTestRunnerCleanupAsync = null,
            Func<string, bool, UnityCliLoopTestMode, TestFilterType, string> appendNoTestsDiagnostics = null,
            Func<int> getActiveHotReloadChangeCount = null,
            Func<UnityCliLoopTestMode, RunTestsTestAsmdefProposal> proposeTestAsmdef = null)
        {
            Debug.Assert(filterService != null, "filterService must not be null");
            Debug.Assert(executionService != null, "executionService must not be null");
            Debug.Assert(validationService != null, "validationService must not be null");
            _filterService = filterService;
            _executionService = executionService;
            _validationService = validationService;
            RunTestsNoTestsDiagnosticService noTestsDiagnosticService = new RunTestsNoTestsDiagnosticService();
            _appendNoTestsDiagnostics = appendNoTestsDiagnostics
                ?? ((string message, bool noTestsFound, UnityCliLoopTestMode testMode, TestFilterType filterType) =>
                    noTestsDiagnosticService.AppendDiagnosticsIfNeeded(message, noTestsFound, testMode, filterType));
            _clearActivePausePoints = clearActivePausePoints ?? ClearActivePausePointsDefault;
            _waitForTestRunnerCleanupAsync = waitForTestRunnerCleanupAsync ?? WaitForTestRunnerCleanupAsync;
            _getActiveHotReloadChangeCount = getActiveHotReloadChangeCount ?? ReadActiveHotReloadChangeCount;
            _proposeTestAsmdef = proposeTestAsmdef ?? RunTestsTestAsmdefProposalBuilder.Propose;
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
            int activeHotReloadChangeCountAtStart = _getActiveHotReloadChangeCount();

            // 2. Test execution
            // Why ConfigureAwait(false) is paired with SwitchToMainThread inside execution helpers:
            // the server is single-flight (BUSY rejects concurrent commands), so no CLI-originated
            // pause can fire during test execution. Off-thread resumes must switch back before any
            // Unity API work (stop/restore, cleanup delay, callback dispose).
            ct.ThrowIfCancellationRequested();
            using CancellationTokenSource timeoutCancellationTokenSource =
                RunTestsExecutionTimeout.CreateLinkedTimeoutSource(ct, parameters.TimeoutSeconds);
            CancellationToken executionCt = timeoutCancellationTokenSource.Token;
            SerializableTestResult result;
            try
            {
                if (parameters.TestMode == UnityCliLoopTestMode.PlayMode)
                {
                    result = await _executionService.ExecutePlayModeTestAsync(filter, executionCt).ConfigureAwait(false);
                }
                else
                {
                    result = await _executionService.ExecuteEditModeTestAsync(filter, executionCt).ConfigureAwait(false);
                }

                // Why parent ct (not executionCt): CancelAfter only guards the RunFinished wait.
                // Using the linked token here would mis-report a successful run as timed out when
                // the fixed cleanup delay straddles the deadline after RunFinished already arrived.
                await MainThreadSwitcher.SwitchToMainThread(ct);
                await _waitForTestRunnerCleanupAsync(ct).ConfigureAwait(false);
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

            CopyTestDetails(result, response);

            response.Warning = RunTestsHotReloadDiscardWarningBuilder.Build(activeHotReloadChangeCountAtStart);

            if (result.failedCount > RunTestsConstants.FailedTestDetailsLimit)
            {
                response.Message = response.Message
                    + " "
                    + string.Format(
                        CultureInfo.InvariantCulture,
                        RunTestsConstants.FailedTestDetailsTruncatedMessageFormat,
                        RunTestsConstants.FailedTestDetailsLimit,
                        result.failedCount);
            }

            // Why switch here: cleanup waits with ConfigureAwait(false), so this resume is
            // off-thread. No-tests diagnostics call AssetDatabase.FindAssets, and the
            // predefined-assembly scan calls TypeCache, both Unity Editor APIs.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            response.Message = RunTestsNoTestsDiagnosticService.AppendDiagnosticsOrOriginalMessage(
                response.Message,
                () => _appendNoTestsDiagnostics(
                    response.Message,
                    response.NoTestsFound,
                    parameters.TestMode,
                    parameters.FilterType));
            AppendPredefinedAssemblyTestNoticeIfNeeded(response, parameters);
            AttachTestAsmdefProposalIfNeeded(response, parameters);
            await ApplyUnfilteredFilterEchoIfNeededAsync(response, parameters, ct)
                .ConfigureAwait(false);
            return response;
        }

        private void CopyTestDetails(SerializableTestResult result, RunTestsResponse response)
        {
            if (result.failedTests != null && result.failedTests.Length > 0)
            {
                response.FailedTests = result.failedTests;
            }

            if (result.skippedTests != null && result.skippedTests.Length > 0)
            {
                response.SkippedTests = result.skippedTests;
            }
        }

        private void AppendPredefinedAssemblyTestNoticeIfNeeded(
            RunTestsResponse response,
            RunTestsSchema parameters)
        {
            Debug.Assert(response != null, "response must not be null");
            Debug.Assert(parameters != null, "parameters must not be null");

            if (!RunTestsNoTestsDiagnosticService.ShouldAppendDiagnostics(
                    response.NoTestsFound,
                    parameters.FilterType))
            {
                return;
            }

            RunTestsPredefinedAssemblyTestFindings findings = _executionService.ScanPredefinedAssemblyTests();
            response.Message = RunTestsPredefinedAssemblyTestNoticeFormatter.AppendIfNeeded(
                response.Message,
                findings);
        }

        // Why after the predefined-assembly notice: that notice names the stranded test methods,
        // and this one tells the caller where to put them.
        private void AttachTestAsmdefProposalIfNeeded(RunTestsResponse response, RunTestsSchema parameters)
        {
            Debug.Assert(response != null, "response must not be null");
            Debug.Assert(parameters != null, "parameters must not be null");

            if (!RunTestsNoTestsDiagnosticService.ShouldAppendDiagnostics(
                    response.NoTestsFound,
                    parameters.FilterType))
            {
                return;
            }

            RunTestsTestAsmdefProposal proposal = RunTestsNoTestsDiagnosticService.InspectAsmdefsOrFallback<RunTestsTestAsmdefProposal>(
                null,
                () => _proposeTestAsmdef(parameters.TestMode));
            if (proposal == null)
            {
                return;
            }

            response.ProposedTestAsmdef = proposal;
            response.Message = RunTestsTestAsmdefProposal.AppendNotice(response.Message, proposal);
        }

        private async Task ApplyUnfilteredFilterEchoIfNeededAsync(
            RunTestsResponse response,
            RunTestsSchema parameters,
            CancellationToken ct)
        {
            if (!response.NoTestsFound || parameters.FilterType == TestFilterType.all)
            {
                return;
            }

            RunTestsUnfilteredTestListResult unfiltered =
                await _executionService.RetrieveUnfilteredTestNamesAsync(parameters.TestMode, ct)
                    .ConfigureAwait(false);
            RunTestsUnfilteredFilterEcho.ApplyIfRetrieved(
                response,
                parameters.FilterType,
                parameters.FilterValue,
                unfiltered);
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

        private static int ReadActiveHotReloadChangeCount()
        {
            Func<int> getter = HotReloadPausePointCoordination.GetActiveHotReloadPatchCount;
            if (getter == null)
            {
                return 0;
            }

            return getter.Invoke();
        }

        private static async Task WaitForTestRunnerCleanupAsync(CancellationToken ct)
        {
            // Why: Unity Test Framework exposes the real active-run signal only through internal API,
            // while the public RunFinished callback fires before cleanup tasks such as RestoreSceneSetupTask.
            await TimerDelay.Wait(TestRunnerCleanupFallbackDelayMilliseconds, ct).ConfigureAwait(false);
        }
    }
}
