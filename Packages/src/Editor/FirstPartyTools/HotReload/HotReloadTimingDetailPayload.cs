using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Values of the hot_reload_timing_detail vibe entry: the run's phases, the named steps outside
    /// them, and the part of the time outside the phases that no step covers.
    /// </summary>
    internal sealed class HotReloadTimingDetailPayload
    {
        private HotReloadTimingDetailPayload(
            HotReloadTimingBreakdown breakdown,
            IReadOnlyList<HotReloadTimingDetailStep> steps,
            long unaccountedMs,
            int groupCount)
        {
            TotalMs = breakdown.TotalMs;
            AnalysisMs = breakdown.AnalysisMs;
            ShimCompileMs = breakdown.ShimCompileMs;
            PatchMs = breakdown.PatchMs;
            OtherMs = breakdown.OtherMs;
            Steps = steps;
            UnaccountedMs = unaccountedMs;
            GroupCount = groupCount;
        }

        public long TotalMs { get; }

        public long AnalysisMs { get; }

        public long ShimCompileMs { get; }

        public long PatchMs { get; }

        public long OtherMs { get; }

        // Negative when the steps add up to more than OtherMs, which means a step overlaps a phase.
        public long UnaccountedMs { get; }

        public int GroupCount { get; }

        public IReadOnlyList<HotReloadTimingDetailStep> Steps { get; }

        public static HotReloadTimingDetailPayload Build(
            HotReloadTimingBreakdown breakdown,
            IReadOnlyList<KeyValuePair<string, long>> details,
            int groupCount)
        {
            if (breakdown == null)
            {
                throw new ArgumentNullException(nameof(breakdown));
            }

            if (details == null)
            {
                throw new ArgumentNullException(nameof(details));
            }

            if (groupCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(groupCount), groupCount, "groupCount must not be negative.");
            }

            List<HotReloadTimingDetailStep> steps = new List<HotReloadTimingDetailStep>(details.Count);
            long stepsMs = 0;
            foreach (KeyValuePair<string, long> detail in details)
            {
                steps.Add(new HotReloadTimingDetailStep(detail.Key, detail.Value));
                stepsMs += detail.Value;
            }

            // Why not clamped at zero: a negative rest is the only sign that a step overlaps a phase.
            return new HotReloadTimingDetailPayload(breakdown, steps, breakdown.OtherMs - stepsMs, groupCount);
        }
    }
}
