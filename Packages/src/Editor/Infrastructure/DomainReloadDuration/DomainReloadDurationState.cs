using System;

using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// The domain reload measurements of one Editor session: the reload in progress, the fastest reload so far,
    /// how many measured reloads in a row have been slow, and the recent finished reloads, oldest first.
    /// </summary>
    internal sealed class DomainReloadDurationState
    {
        internal static readonly DomainReloadDurationState Empty = new DomainReloadDurationState(
            pendingStartUtcTicks: null,
            pendingIsMeasured: false,
            measuredReloadCount: 0,
            baselineSeconds: null,
            slowStreak: 0,
            recentReloads: Array.Empty<DomainReloadRecord>());

        [JsonConstructor]
        public DomainReloadDurationState(
            long? pendingStartUtcTicks,
            bool pendingIsMeasured,
            int measuredReloadCount,
            double? baselineSeconds,
            int slowStreak,
            DomainReloadRecord[] recentReloads)
        {
            if (measuredReloadCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(measuredReloadCount), "measuredReloadCount must not be negative");
            }

            if (slowStreak < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slowStreak), "slowStreak must not be negative");
            }

            if (pendingIsMeasured && !pendingStartUtcTicks.HasValue)
            {
                throw new ArgumentException("A measured pending reload needs its start.");
            }

            if (slowStreak > 0 && !baselineSeconds.HasValue)
            {
                throw new ArgumentException("A slow streak needs the baseline it was measured against.");
            }

            PendingStartUtcTicks = pendingStartUtcTicks;
            PendingIsMeasured = pendingIsMeasured;
            MeasuredReloadCount = measuredReloadCount;
            BaselineSeconds = baselineSeconds;
            SlowStreak = slowStreak;
            RecentReloads = recentReloads ?? Array.Empty<DomainReloadRecord>();
        }

        /// <summary>
        /// When the reload in progress started. Null when no reload is in progress.
        /// </summary>
        public long? PendingStartUtcTicks { get; }

        /// <summary>
        /// Whether the reload in progress counts toward the baseline and the slow streak.
        /// </summary>
        public bool PendingIsMeasured { get; }

        public int MeasuredReloadCount { get; }

        /// <summary>
        /// The fastest measured reload after the warm-up reloads. Null until the first reload after them.
        /// </summary>
        public double? BaselineSeconds { get; }

        /// <summary>
        /// How many of the latest measured reloads in a row took at least twice the baseline.
        /// </summary>
        public int SlowStreak { get; }

        /// <summary>
        /// The finished reloads, measured or not, still within the retention window, oldest first.
        /// </summary>
        public DomainReloadRecord[] RecentReloads { get; }

        internal DomainReloadDurationState WithPendingStart(long pendingStartUtcTicks, bool pendingIsMeasured)
        {
            return new DomainReloadDurationState(
                pendingStartUtcTicks,
                pendingIsMeasured,
                MeasuredReloadCount,
                BaselineSeconds,
                SlowStreak,
                RecentReloads);
        }
    }
}
