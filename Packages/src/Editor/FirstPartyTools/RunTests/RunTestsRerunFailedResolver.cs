using System;
using System.Globalization;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides from the last-run record whether a --rerun-failed request runs, and which tests it runs.
    /// </summary>
    internal static class RunTestsRerunFailedResolver
    {
        internal const string FilterConflictMessage =
            "--rerun-failed cannot be combined with --filter-type or --filter-value; it reruns the failures recorded for the test mode.";

        // Format: TestMode.
        internal const string NoRecordMessageFormat =
            "No completed {0} run is recorded for this project. Run uloop run-tests without --rerun-failed first.";

        // Format: TestMode, the reason the record was rejected.
        internal const string UnreadableRecordMessageFormat =
            "The recorded {0} run could not be read ({1}). Run uloop run-tests without --rerun-failed.";

        // Format: number of recorded targets, TestMode, CompletedAt of the recorded run.
        internal const string RerunTargetsMissingMessageFormat =
            "None of the {0} tests recorded as failed in the {1} run completed at {2} exist any more; they were renamed or removed. Run uloop run-tests without --rerun-failed.";

        /// <summary>
        /// Resolves a --rerun-failed request: the recorded failures as a TestNames filter, or the
        /// response to return without running when there is nothing it can safely rerun.
        /// </summary>
        internal static RunTestsExecutionTarget Resolve(RunTestsSchema parameters, RunTestsLastRunRecordStore store)
        {
            if (parameters == null)
            {
                throw new ArgumentNullException(nameof(parameters));
            }

            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (!parameters.RerunFailed)
            {
                throw new ArgumentException("Only a --rerun-failed request can be resolved here.", nameof(parameters));
            }

            if (parameters.FilterType != TestFilterType.all || !string.IsNullOrEmpty(parameters.FilterValue))
            {
                return StopWithFailure(FilterConflictMessage);
            }

            RunTestsLastRunRecordReadResult read = store.Read(parameters.TestMode);
            if (read.Status == RunTestsLastRunRecordReadStatus.Missing)
            {
                return StopWithFailure(Format(NoRecordMessageFormat, parameters.TestMode));
            }

            if (read.Status == RunTestsLastRunRecordReadStatus.Unreadable)
            {
                return StopWithFailure(
                    Format(UnreadableRecordMessageFormat, parameters.TestMode, read.UnreadableReason));
            }

            string[] rerunTargets = read.Record.RerunTargets;
            if (rerunTargets.Length == 0)
            {
                return RunTestsExecutionTarget.Stop(
                    RunTestsResponse.CreateNothingToRerun(parameters.TestMode, read.Record.CompletedAt));
            }

            return RunTestsExecutionTarget.Run(
                TestExecutionFilter.ByTestNames(rerunTargets),
                new RunTestsRerunSource(rerunTargets.Length, read.Record.CompletedAt));
        }

        /// <summary>
        /// The message for a rerun whose recorded tests all matched nothing.
        /// </summary>
        internal static string FormatRerunTargetsMissingMessage(
            int targetCount,
            UnityCliLoopTestMode testMode,
            string sourceCompletedAt)
        {
            return Format(RerunTargetsMissingMessageFormat, targetCount, testMode, sourceCompletedAt);
        }

        private static RunTestsExecutionTarget StopWithFailure(string message)
        {
            return RunTestsExecutionTarget.Stop(
                RunTestsUseCase.CreateFailureResponse(message, RunTestsUseCase.NoHotReloadChangesObserved));
        }

        private static string Format(string format, params object[] arguments)
        {
            return string.Format(CultureInfo.InvariantCulture, format, arguments);
        }
    }

    /// <summary>
    /// What a run-tests request resolved to before anything changed: the filter to run with, or the
    /// response to return without running.
    /// </summary>
    internal sealed class RunTestsExecutionTarget
    {
        /// <summary>
        /// The filter to run with; null runs every test of the test mode.
        /// </summary>
        public TestExecutionFilter Filter { get; }

        /// <summary>
        /// The recorded run a --rerun-failed run reruns; null for every other run.
        /// </summary>
        public RunTestsRerunSource RerunSource { get; }

        /// <summary>
        /// The response to return without running; null when the tests run.
        /// </summary>
        public RunTestsResponse EarlyResponse { get; }

        private RunTestsExecutionTarget(
            TestExecutionFilter filter,
            RunTestsRerunSource rerunSource,
            RunTestsResponse earlyResponse)
        {
            Filter = filter;
            RerunSource = rerunSource;
            EarlyResponse = earlyResponse;
        }

        public static RunTestsExecutionTarget Stop(RunTestsResponse earlyResponse)
        {
            if (earlyResponse == null)
            {
                throw new ArgumentNullException(nameof(earlyResponse));
            }

            return new RunTestsExecutionTarget(null, null, earlyResponse);
        }

        public static RunTestsExecutionTarget Run(TestExecutionFilter filter, RunTestsRerunSource rerunSource)
        {
            return new RunTestsExecutionTarget(filter, rerunSource, null);
        }
    }

    /// <summary>
    /// The recorded run that a --rerun-failed run reruns, as reported back in its response.
    /// </summary>
    internal sealed class RunTestsRerunSource
    {
        public int TargetCount { get; }

        public string SourceCompletedAt { get; }

        public RunTestsRerunSource(int targetCount, string sourceCompletedAt)
        {
            TargetCount = targetCount;
            SourceCompletedAt = sourceCompletedAt;
        }
    }
}
