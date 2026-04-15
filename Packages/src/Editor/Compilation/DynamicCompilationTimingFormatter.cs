using System.Collections.Generic;

namespace io.github.hatayama.uLoopMCP
{
    internal static class DynamicCompilationTimingFormatter
    {
        private const string CacheHitTimingEntry = "[Perf] CacheHit: true";

        public static List<string> CreateCompilationTimings(
            double referenceResolutionMilliseconds,
            double buildMilliseconds,
            double assemblyLoadMilliseconds,
            DynamicCompilationBackendKind backendKind = DynamicCompilationBackendKind.Unknown)
        {
            List<string> timings = new List<string>
            {
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

        public static List<string> CreateCachedCompilationTimings(
            DynamicCompilationBackendKind backendKind = DynamicCompilationBackendKind.Unknown)
        {
            List<string> timings = CreateCompilationTimings(0, 0, 0, backendKind);
            timings.Add(CacheHitTimingEntry);
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
