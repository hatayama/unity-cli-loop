using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Records the compile result that the CLI reads after Unity finishes Domain Reload.
    /// </summary>
    public sealed class UnityCliLoopStoredCompileResult
    {
        private UnityCliLoopStoredCompileResult(
            bool hasResult,
            string requestId,
            bool forceRecompile,
            string resultJson,
            long completedAtUtcTicks,
            bool isFollowedByReload)
        {
            HasResult = hasResult;
            RequestId = requestId;
            ForceRecompile = forceRecompile;
            ResultJson = resultJson;
            CompletedAtUtcTicks = completedAtUtcTicks;
            IsFollowedByReload = isFollowedByReload;
        }

        public bool HasResult { get; }
        public string RequestId { get; }
        public bool ForceRecompile { get; }
        public string ResultJson { get; }
        public long CompletedAtUtcTicks { get; }

        /// <summary>
        /// Whether Unity reloads the domain after this result was stored because of this compile: the compile
        /// ran and had no errors. False for a compile that was skipped, stopped, or failed.
        /// </summary>
        public bool IsFollowedByReload { get; }

        public static UnityCliLoopStoredCompileResult None()
        {
            return new UnityCliLoopStoredCompileResult(false, "", false, "", 0, false);
        }

        public static UnityCliLoopStoredCompileResult Create(
            string requestId,
            bool forceRecompile,
            string resultJson,
            long completedAtUtcTicks,
            bool isFollowedByReload)
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

            return new UnityCliLoopStoredCompileResult(
                true,
                requestId,
                forceRecompile,
                resultJson,
                completedAtUtcTicks,
                isFollowedByReload);
        }

        public bool IsExpiredAt(DateTime utcNow, TimeSpan lifetime)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");
            Debug.Assert(lifetime > TimeSpan.Zero, "lifetime must be positive");
            return HasResult && CompletedAtUtcTicks <= (utcNow - lifetime).Ticks;
        }
    }
}
