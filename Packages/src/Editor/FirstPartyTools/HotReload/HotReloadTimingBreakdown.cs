using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Milliseconds an apply run spent in each phase, as reported in the response.
    /// </summary>
    internal sealed class HotReloadTimingBreakdown
    {
        public HotReloadTimingBreakdown(long analysisMs, long shimCompileMs, long patchMs, long totalMs)
        {
            AnalysisMs = RequireElapsed(analysisMs, nameof(analysisMs));
            ShimCompileMs = RequireElapsed(shimCompileMs, nameof(shimCompileMs));
            PatchMs = RequireElapsed(patchMs, nameof(patchMs));
            TotalMs = RequireElapsed(totalMs, nameof(totalMs));

            // Why refuse instead of clamping: the phases are spans inside the run's total, so a
            // shorter total means a phase was measured outside the run.
            long otherMs = totalMs - analysisMs - shimCompileMs - patchMs;
            if (otherMs < 0)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(totalMs),
                    totalMs,
                    "Total milliseconds must cover the phases.");
            }

            OtherMs = otherMs;
        }

        // Transform worker runs, introduced-type preparation included.
        public long AnalysisMs { get; }

        // The signature-change gate and the shim compile, isolation retries included.
        public long ShimCompileMs { get; }

        // Applying the patches.
        public long PatchMs { get; }

        // Outside the three phases: file resolution, planning, the checks that find unchanged
        // methods, and the like.
        public long OtherMs { get; }

        // The whole run, the three phases included.
        public long TotalMs { get; }

        internal static long RequireElapsed(long milliseconds, string parameterName)
        {
            if (milliseconds < 0)
            {
                throw new ArgumentOutOfRangeException(
                    parameterName,
                    milliseconds,
                    "Elapsed milliseconds must not be negative.");
            }

            return milliseconds;
        }
    }
}
