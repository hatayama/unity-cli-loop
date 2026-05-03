using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    internal sealed class CompiledAssemblyBuildResult
    {
        public string UpdatedSource { get; }

        public CompilerDiagnostics Diagnostics { get; }

        public Dictionary<string, List<string>> AmbiguousTypeCandidates { get; }

        public List<string> AutoInjectedNamespaces { get; }

        public byte[] AssemblyBytes { get; }

        public double ReferenceResolutionMilliseconds { get; }

        public double BuildMilliseconds { get; }

        public int BuildCount { get; }

        public bool ShouldCacheResult { get; }

        public DynamicCompilationBackendKind CompilationBackendKind { get; }

        public CompiledAssemblyBuildResult(
            string updatedSource,
            CompilerDiagnostics diagnostics,
            Dictionary<string, List<string>> ambiguousTypeCandidates,
            List<string> autoInjectedNamespaces,
            byte[] assemblyBytes,
            double referenceResolutionMilliseconds,
            double buildMilliseconds,
            int buildCount,
            bool shouldCacheResult,
            DynamicCompilationBackendKind compilationBackendKind)
        {
            UpdatedSource = updatedSource;
            Diagnostics = diagnostics;
            AmbiguousTypeCandidates = ambiguousTypeCandidates;
            AutoInjectedNamespaces = autoInjectedNamespaces;
            AssemblyBytes = assemblyBytes;
            ReferenceResolutionMilliseconds = referenceResolutionMilliseconds;
            BuildMilliseconds = buildMilliseconds;
            BuildCount = buildCount;
            ShouldCacheResult = shouldCacheResult;
            CompilationBackendKind = compilationBackendKind;
        }
    }
}
