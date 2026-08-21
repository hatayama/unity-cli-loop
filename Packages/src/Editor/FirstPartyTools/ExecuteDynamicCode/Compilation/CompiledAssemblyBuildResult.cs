using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the result data produced by Compiled Assembly Build behavior.
    /// </summary>
    public sealed class CompiledAssemblyBuildResult
    {
        public string UpdatedSource { get; }

        public CompilerDiagnostics Diagnostics { get; }

        public Dictionary<string, List<string>> AmbiguousTypeCandidates { get; }

        public List<AutoInjectedNamespace> AutoInjectedNamespaces { get; }

        public byte[] AssemblyBytes { get; }

        /// <summary>
        /// Portable PDB bytes when the compiler emitted them; null for AssemblyBuilder fallback
        /// or failed builds. Loaded with AssemblyBytes so runtime stacks can name user-snippet.cs.
        /// </summary>
        public byte[] PdbBytes { get; }

        public double ReferenceResolutionMilliseconds { get; }

        public double BuildMilliseconds { get; }

        public int BuildCount { get; }

        public bool ShouldCacheResult { get; }

        public DynamicCompilationBackendKind CompilationBackendKind { get; }

        public CompiledAssemblyBuildResult(
            string updatedSource,
            CompilerDiagnostics diagnostics,
            Dictionary<string, List<string>> ambiguousTypeCandidates,
            List<AutoInjectedNamespace> autoInjectedNamespaces,
            byte[] assemblyBytes,
            byte[] pdbBytes,
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
            PdbBytes = pdbBytes;
            ReferenceResolutionMilliseconds = referenceResolutionMilliseconds;
            BuildMilliseconds = buildMilliseconds;
            BuildCount = buildCount;
            ShouldCacheResult = shouldCacheResult;
            CompilationBackendKind = compilationBackendKind;
        }
    }
}
