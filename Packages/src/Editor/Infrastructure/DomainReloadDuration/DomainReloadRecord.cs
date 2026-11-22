using System;

using Newtonsoft.Json;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// One finished domain reload: when it started, how long it took, and the fastest reload and slow streak as of
    /// that reload, so a compile result read later reports this reload as it was.
    /// </summary>
    internal sealed class DomainReloadRecord
    {
        [JsonConstructor]
        public DomainReloadRecord(long startedAtUtcTicks, double? seconds, double? baselineSeconds, int slowStreak)
        {
            if (slowStreak < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(slowStreak), "slowStreak must not be negative");
            }

            if (slowStreak > 0 && !baselineSeconds.HasValue)
            {
                throw new ArgumentException("A slow streak needs the baseline it was measured against.");
            }

            StartedAtUtcTicks = startedAtUtcTicks;
            Seconds = seconds;
            BaselineSeconds = baselineSeconds;
            SlowStreak = slowStreak;
        }

        public long StartedAtUtcTicks { get; }

        /// <summary>
        /// How long the reload took. Null when it was not measured.
        /// </summary>
        public double? Seconds { get; }

        public double? BaselineSeconds { get; }

        public int SlowStreak { get; }
    }
}
