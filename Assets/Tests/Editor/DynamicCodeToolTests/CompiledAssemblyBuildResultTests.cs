using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Characterizes CompiledAssemblyBuildResult DTO fields used for PDB propagation.
    /// </summary>
    [TestFixture]
    public sealed class CompiledAssemblyBuildResultTests
    {
        /// <summary>
        /// Verifies build results expose PDB bytes alongside assembly bytes for the loader.
        /// </summary>
        [Test]
        public void Constructor_WhenPdbBytesProvided_ExposesPdbBytesOnResult()
        {
            byte[] assemblyBytes = { 0x4D, 0x5A };
            byte[] pdbBytes = { 0x42, 0x53, 0x4A, 0x42 };
            CompilerDiagnostics diagnostics = CompilerDiagnostics.FromMessages(System.Array.Empty<CompilerMessage>());

            CompiledAssemblyBuildResult result = new(
                updatedSource: "return 1;",
                diagnostics: diagnostics,
                ambiguousTypeCandidates: new Dictionary<string, List<string>>(),
                autoInjectedNamespaces: new List<string>(),
                assemblyBytes: assemblyBytes,
                pdbBytes: pdbBytes,
                referenceResolutionMilliseconds: 1d,
                buildMilliseconds: 2d,
                buildCount: 1,
                shouldCacheResult: true,
                compilationBackendKind: DynamicCompilationBackendKind.SharedRoslynWorker);

            Assert.That(result.AssemblyBytes, Is.SameAs(assemblyBytes));
            Assert.That(result.PdbBytes, Is.SameAs(pdbBytes));
            Assert.That(result.PdbBytes.Length, Is.EqualTo(4));
        }

        /// <summary>
        /// Verifies null PDB bytes remain null so AssemblyBuilder fallback can skip symbols.
        /// </summary>
        [Test]
        public void Constructor_WhenPdbBytesNull_ExposesNullPdbBytes()
        {
            CompilerDiagnostics diagnostics = CompilerDiagnostics.FromMessages(System.Array.Empty<CompilerMessage>());

            CompiledAssemblyBuildResult result = new(
                updatedSource: "return 1;",
                diagnostics: diagnostics,
                ambiguousTypeCandidates: new Dictionary<string, List<string>>(),
                autoInjectedNamespaces: new List<string>(),
                assemblyBytes: new byte[] { 0x4D, 0x5A },
                pdbBytes: null,
                referenceResolutionMilliseconds: 0d,
                buildMilliseconds: 0d,
                buildCount: 1,
                shouldCacheResult: false,
                compilationBackendKind: DynamicCompilationBackendKind.AssemblyBuilderFallback);

            Assert.That(result.PdbBytes, Is.Null);
        }
    }
}
