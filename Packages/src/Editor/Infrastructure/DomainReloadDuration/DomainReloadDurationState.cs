using System;

using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// The domain reload measurements of one Editor session: the reload being measured, the fastest reload so far,
    /// how many reloads in a row have been slow, and the latest measured reload.
    /// </summary>
    internal sealed class DomainReloadDurationState
    {
        internal static readonly DomainReloadDurationState Empty = new DomainReloadDurationState(
            pendingStartUtcTicks: null,
            measuredReloadCount: 0,
            baselineSeconds: null,
            slowStreak: 0,
            lastReloadStartedAtUtcTicks: null,
            lastReloadSeconds: null);

        [JsonConstructor]
        public DomainReloadDurationState(
            long? pendingStartUtcTicks,
            int measuredReloadCount,
            double? baselineSeconds,
            int slowStreak,
            long? lastReloadStartedAtUtcTicks,
            double? lastReloadSeconds)
        {
            if (measuredReloadCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(measuredReloadCount), "measuredReloadCount must not be negative");
            }

            if (slowStreak < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slowStreak), "slowStreak must not be negative");
            }

            if (lastReloadStartedAtUtcTicks.HasValue != lastReloadSeconds.HasValue)
            {
                throw new ArgumentException("The latest reload needs both its start and its duration, or neither.");
            }

            PendingStartUtcTicks = pendingStartUtcTicks;
            MeasuredReloadCount = measuredReloadCount;
            BaselineSeconds = baselineSeconds;
            SlowStreak = slowStreak;
            LastReloadStartedAtUtcTicks = lastReloadStartedAtUtcTicks;
            LastReloadSeconds = lastReloadSeconds;
        }

        /// <summary>
        /// When the reload being measured started. Null when no measured reload is in progress.
        /// </summary>
        public long? PendingStartUtcTicks { get; }

        public int MeasuredReloadCount { get; }

        /// <summary>
        /// The fastest reload after the warm-up reloads. Null until the first reload after them.
        /// </summary>
        public double? BaselineSeconds { get; }

        /// <summary>
        /// How many of the latest reloads in a row took at least twice the baseline.
        /// </summary>
        public int SlowStreak { get; }

        public long? LastReloadStartedAtUtcTicks { get; }

        public double? LastReloadSeconds { get; }

        internal DomainReloadDurationState WithPendingStart(long? pendingStartUtcTicks)
        {
            return new DomainReloadDurationState(
                pendingStartUtcTicks,
                MeasuredReloadCount,
                BaselineSeconds,
                SlowStreak,
                LastReloadStartedAtUtcTicks,
                LastReloadSeconds);
        }
    }
}
