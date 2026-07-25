using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Formats Dynamic Compilation Timing data for output to callers.
    /// </summary>
    internal static class DynamicCompilationTimingFormatter
    {
        public static List<string> CreateCompilationTimings(
            double referenceResolutionMilliseconds,
            double buildMilliseconds,
            double assemblyLoadMilliseconds,
            DynamicCompilationBackendKind backendKind = DynamicCompilationBackendKind.Unknown)
        {
            List<string> timings = new()            {
                $"[Perf] ReferenceResolution: {referenceResolutionMilliseconds:F1}ms",
                $"[Perf] Build: {buildMilliseconds:F1}ms",
                $"[Perf] AssemblyLoad: {assemblyLoadMilliseconds:F1}ms"
            };

            string backendTimingEntry = CreateBackendTimingEntry(backendKind);
            if (!string.IsNullOrEmpty(backendTimingEntry))
            {
                timings.Add(backendTimingEntry);
            }

            return timings;
        }

        private static string CreateBackendTimingEntry(DynamicCompilationBackendKind backendKind)
        {
            return backendKind switch
            {
                DynamicCompilationBackendKind.SharedRoslynWorker => "[Perf] Backend: SharedRoslynWorker",
                DynamicCompilationBackendKind.OneShotRoslyn => "[Perf] Backend: OneShotRoslyn",
                DynamicCompilationBackendKind.AssemblyBuilderFallback => "[Perf] Backend: AssemblyBuilderFallback",
                _ => null
            };
        }
    }
}
