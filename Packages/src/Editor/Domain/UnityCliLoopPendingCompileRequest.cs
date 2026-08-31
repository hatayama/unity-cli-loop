using System;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Records a compile request that may need an indeterminate result after Domain Reload.
    /// </summary>
    public sealed class UnityCliLoopPendingCompileRequest
    {
        private UnityCliLoopPendingCompileRequest(
            bool hasRequest,
            string requestId,
            bool forceRecompile,
            long expiresAtUtcTicks,
            bool reloadObserved)
        {
            HasRequest = hasRequest;
            RequestId = requestId;
            ForceRecompile = forceRecompile;
            ExpiresAtUtcTicks = expiresAtUtcTicks;
            ReloadObserved = reloadObserved;
        }

        public bool HasRequest { get; }
        public string RequestId { get; }
        public bool ForceRecompile { get; }
        public long ExpiresAtUtcTicks { get; }
        public bool ReloadObserved { get; }

        public static UnityCliLoopPendingCompileRequest None()
        {
            return new UnityCliLoopPendingCompileRequest(false, "", false, 0, false);
        }

        public static UnityCliLoopPendingCompileRequest Create(
            string requestId,
            bool forceRecompile,
            long expiresAtUtcTicks,
            bool reloadObserved)
        {
            Debug.Assert(!string.IsNullOrWhiteSpace(requestId), "requestId must not be null or whitespace");
            Debug.Assert(expiresAtUtcTicks > 0, "expiresAtUtcTicks must be positive");

            if (string.IsNullOrWhiteSpace(requestId))
            {
                throw new ArgumentException("requestId must not be null or whitespace.", nameof(requestId));
            }

            return new UnityCliLoopPendingCompileRequest(
                true,
                requestId,
                forceRecompile,
                expiresAtUtcTicks,
                reloadObserved);
        }

        public bool IsExpiredAt(DateTime utcNow)
        {
            Debug.Assert(utcNow.Kind == DateTimeKind.Utc, "utcNow must be UTC");
            return HasRequest && ExpiresAtUtcTicks <= utcNow.Ticks;
        }
    }
}
