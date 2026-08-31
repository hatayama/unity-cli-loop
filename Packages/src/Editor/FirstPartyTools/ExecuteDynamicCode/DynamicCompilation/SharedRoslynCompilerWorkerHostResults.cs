using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    internal static class SharedWorkerFailureReasons
    {
        internal const string LifecycleClosed = "shared_worker_lifecycle_closed";
    }

    /// <summary>
    /// Carries shared worker compilation messages and the reason for a failed compilation.
    /// </summary>
    internal sealed class SharedWorkerCompileOutcome
    {
        public CompilerMessage[] Messages { get; }

        public string FailureReason { get; }

        public object FailureContext { get; }

        private SharedWorkerCompileOutcome(
            CompilerMessage[] messages,
            string failureReason,
            object failureContext)
        {
            Messages = messages;
            FailureReason = failureReason;
            FailureContext = failureContext;
        }

        public bool Succeeded => Messages != null;

        public bool IsLifecycleClosed => FailureReason == SharedWorkerFailureReasons.LifecycleClosed;

        public static SharedWorkerCompileOutcome SucceededWith(CompilerMessage[] messages)
        {
            return new SharedWorkerCompileOutcome(messages, null, null);
        }

        public static SharedWorkerCompileOutcome Failed(string failureReason, object failureContext)
        {
            return new SharedWorkerCompileOutcome(null, failureReason, failureContext);
        }
    }

    /// <summary>
    /// Carries the result data produced by Worker Attempt behavior.
    /// </summary>
    internal sealed class WorkerAttemptResult
    {
        public CompilerMessage[] Messages { get; }

        public bool ShouldRetry { get; }

        public string FailureReason { get; }

        public object FailureContext { get; }

        private WorkerAttemptResult(
            CompilerMessage[] messages,
            bool shouldRetry,
            string failureReason,
            object failureContext)
        {
            Messages = messages;
            ShouldRetry = shouldRetry;
            FailureReason = failureReason;
            FailureContext = failureContext;
        }

        public bool Succeeded => Messages != null;

        public static WorkerAttemptResult Successful(CompilerMessage[] messages)
        {
            return new WorkerAttemptResult(messages, false, null, null);
        }

        public static WorkerAttemptResult RetryableFailure(string failureReason, object failureContext)
        {
            return new WorkerAttemptResult(null, true, failureReason, failureContext);
        }

        public static WorkerAttemptResult NonRetryableFailure(string failureReason, object failureContext)
        {
            return new WorkerAttemptResult(null, false, failureReason, failureContext);
        }
    }

    /// <summary>
    /// Carries the result data produced by Worker Startup behavior.
    /// </summary>
    internal sealed class WorkerStartupResult
    {
        public bool IsReady { get; }

        public bool IsRetryable { get; }

        public string FailureReason { get; }

        public object FailureContext { get; }

        private WorkerStartupResult(
            bool isReady,
            bool isRetryable,
            string failureReason,
            object failureContext)
        {
            IsReady = isReady;
            IsRetryable = isRetryable;
            FailureReason = failureReason;
            FailureContext = failureContext;
        }

        public static WorkerStartupResult Ready()
        {
            return new WorkerStartupResult(true, false, null, null);
        }

        public static WorkerStartupResult Failure(string failureReason, object failureContext)
        {
            return new WorkerStartupResult(false, true, failureReason, failureContext);
        }

        public static WorkerStartupResult ClosedLifecycleFailure()
        {
            return new WorkerStartupResult(
                false,
                false,
                SharedWorkerFailureReasons.LifecycleClosed,
                new { reason = "lifecycle_generation_advanced" });
        }
    }
}
