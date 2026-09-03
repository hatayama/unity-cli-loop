using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Records the run-tests result that the CLI reads after Unity finishes Domain Reload.
    /// </summary>
    public sealed class UnityCliLoopStoredRunTestsResult
    {
        private UnityCliLoopStoredRunTestsResult(
            bool hasResult,
            string requestId,
            string resultJson,
            long completedAtUtcTicks)
        {
            HasResult = hasResult;
            RequestId = requestId;
            ResultJson = resultJson;
            CompletedAtUtcTicks = completedAtUtcTicks;
        }

        public bool HasResult { get; }
        public string RequestId { get; }
        public string ResultJson { get; }
        public long CompletedAtUtcTicks { get; }

        public static UnityCliLoopStoredRunTestsResult None()
        {
            return new UnityCliLoopStoredRunTestsResult(false, "", "", 0);
        }

        public static UnityCliLoopStoredRunTestsResult Create(
            string requestId,
            string resultJson,
            long completedAtUtcTicks)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(!string.IsNullOrWhiteSpace(resultJson), "resultJson must not be null or whitespace");
            Debug.Assert(completedAtUtcTicks > 0, "completedAtUtcTicks must be positive");

            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("requestId must not be null or whitespace.", nameof(requestId));
            }

            if (string.IsNullOrWhiteSpace(resultJson))
            {
                throw new ArgumentException("resultJson must not be null or whitespace.", nameof(resultJson));
            }

            return new UnityCliLoopStoredRunTestsResult(
                true,
                requestId,
                resultJson,
                completedAtUtcTicks);
        }

        public bool IsExpiredAt(DateTime utcNow, TimeSpan lifetime)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");
            Debug.Assert(lifetime > TimeSpan.Zero, "lifetime must be positive");
            return HasResult && CompletedAtUtcTicks <= (utcNow - lifetime).Ticks;
        }
    }
}
