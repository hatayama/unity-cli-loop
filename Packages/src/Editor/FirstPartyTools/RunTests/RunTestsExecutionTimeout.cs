using System;
using System.Diagnostics;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Bounds run-tests execution with a linked CancelAfter timeout so a missing
    /// RunFinished callback cannot hold the single-flight tool slot until the CLI
    /// absolute response limit.
    /// </summary>
    internal static class RunTestsExecutionTimeout
    {
        /// <summary>
        /// Default budget for hung Test Runner detection. Chosen to free the slot well
        /// before the CLI 30-minute absolute response timeout while leaving headroom
        /// for normal agent-driven suites (seconds to a few minutes).
        /// </summary>
        public const int DefaultTimeoutSeconds = 600;

        /// <summary>
        /// Upper bound kept below the CLI finalResponseTimeout (30 minutes) so the C#
        /// CancelAfter always fires first when a caller asks for a long wait.
        /// </summary>
        public const int MaxTimeoutSeconds = 1500;

        /// <summary>
        /// Validates TimeoutSeconds before creating a CancelAfter source.
        /// </summary>
        public static bool TryValidate(int timeoutSeconds, out string errorMessage)
        {
            if (timeoutSeconds <= 0)
            {
                errorMessage =
                    "TimeoutSeconds must be greater than zero. " +
                    "Pass --timeout-seconds with a positive integer.";
                return false;
            }

            if (timeoutSeconds > MaxTimeoutSeconds)
            {
                errorMessage =
                    $"TimeoutSeconds must be less than or equal to {MaxTimeoutSeconds} " +
                    $"(CLI absolute response limit is 30 minutes). " +
                    $"Received: {timeoutSeconds}.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        /// <summary>
        /// Creates a linked token source that cancels after timeoutSeconds.
        /// </summary>
        public static CancellationTokenSource CreateLinkedTimeoutSource(
            CancellationToken parentCancellationToken,
            int timeoutSeconds)
        {
            Debug.Assert(
                timeoutSeconds > 0 && timeoutSeconds <= MaxTimeoutSeconds,
                "timeoutSeconds must be validated with TryValidate before CreateLinkedTimeoutSource");

            CancellationTokenSource linkedCancellationTokenSource =
                CancellationTokenSource.CreateLinkedTokenSource(parentCancellationToken);
            // Why CancelAfter here: frees the single-flight slot when RunFinished never fires.
            // PR 1-1 only completes the await via cancellation; TestRunnerApi may keep running
            // until PR 1-2 routes this same cancel path through an explicit stop + PlayMode restore.
            linkedCancellationTokenSource.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
            return linkedCancellationTokenSource;
        }

        /// <summary>
        /// Builds the agent-facing message for a CancelAfter timeout.
        /// </summary>
        public static string CreateTimeoutMessage(int timeoutSeconds)
        {
            Debug.Assert(timeoutSeconds > 0, "timeoutSeconds must be greater than zero");

            return
                $"run-tests timed out after {timeoutSeconds} seconds without a RunFinished callback. " +
                "The Unity Test Runner may still be running in the background. " +
                "Increase --timeout-seconds for long suites, or restart with `uloop launch -r` if Unity is stuck.";
        }

        /// <summary>
        /// True when cancellation came from CancelAfter (or another linked child) rather
        /// than the parent request/disconnect token.
        /// </summary>
        public static bool IsTimeoutCancellation(CancellationToken parentCancellationToken)
        {
            return !parentCancellationToken.IsCancellationRequested;
        }
    }
}
