using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    internal static class ExecuteDynamicCodeResponseTimingAugmenter
    {
        public static void AppendTimingEntries(
            ExecuteDynamicCodeResponse response,
            double mainThreadWaitMilliseconds,
            double toolTotalMilliseconds,
            double requestTotalMilliseconds)
        {
            if (response == null)
            {
                return;
            }

            if (response.Timings == null)
            {
                response.Timings = new List<string>();
            }

            response.Timings.Add($"[Perf] MainThreadWait: {mainThreadWaitMilliseconds:F1}ms");
            response.Timings.Add($"[Perf] ToolTotal: {toolTotalMilliseconds:F1}ms");
            response.Timings.Add($"[Perf] RequestTotal: {requestTotalMilliseconds:F1}ms");
            response.Timings.AddRange(DynamicCodeStartupTelemetry.CreateTimingEntries());
        }
    }
}
