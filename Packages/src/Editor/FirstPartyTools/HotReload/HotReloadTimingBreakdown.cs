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
        }

        // Transform worker runs, introduced-type preparation included.
        public long AnalysisMs { get; }

        // The signature-change gate and the shim compile, isolation retries included.
        public long ShimCompileMs { get; }

        // Applying the patches.
        public long PatchMs { get; }

        // The whole run, which also covers file resolution and planning, so it exceeds the sum.
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
