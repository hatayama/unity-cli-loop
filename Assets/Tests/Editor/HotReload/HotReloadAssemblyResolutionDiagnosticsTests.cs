using System;
using System.Collections.Generic;

using NUnit.Framework;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers assembly-resolution Failed reasons for uncompiled or unimported hot-reload targets.
    /// </summary>
    public sealed class HotReloadAssemblyResolutionDiagnosticsTests
    {
        private const string MissingHotReloadScriptPath =
            "Assets/Tests/Editor/HotReload/UncompiledNewScript.cs";
        private const string ExistingHotReloadScriptPath =
            "Assets/Tests/Editor/HotReload/HotReloadToolTests.cs";
        private const string HotReloadTestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";

        /// <summary>
        /// What: a resolved assembly that is absent from CompilationPipeline gets the
        /// compile-first reason that names the fallback predefined-assembly behavior.
        /// </summary>
        [Test]
        public void TryGetAssemblyResolutionFailureReason_WhenCompilationAssemblyIsNull_ReturnsNotFoundReason()
        {
            string reason = HotReloadAssemblyResolutionDiagnostics.TryGetAssemblyResolutionFailureReason(
                "Assembly-CSharp",
                null,
                MissingHotReloadScriptPath,
                false);

            Assert.That(
                reason,
                Is.EqualTo(
                    "Resolved assembly 'Assembly-CSharp' was not found in the compilation pipeline. Unity resolves files under a not-yet-imported .asmdef to a predefined assembly, so a brand-new .asmdef or a brand-new script cannot be hot-reloaded. Run 'uloop compile' first."));
        }

        /// <summary>
        /// What: a missing compilation assembly under an on-disk .asmdef that Unity has not
        /// imported yet gets the more specific unimported-asmdef reason.
        /// </summary>
        [Test]
        public void TryGetAssemblyResolutionFailureReason_WhenUnimportedAsmdefOnDisk_ReturnsSpecificReason()
        {
            string reason = HotReloadAssemblyResolutionDiagnostics.TryGetAssemblyResolutionFailureReason(
                "Assembly-CSharp",
                null,
                MissingHotReloadScriptPath,
                true);

            Assert.That(
                reason,
                Is.EqualTo(
                    "Resolved assembly 'Assembly-CSharp' was not found in the compilation pipeline. 'Assets/Tests/Editor/HotReload/UncompiledNewScript.cs' sits under a .asmdef that Unity has not imported yet, so hot reload cannot target it. Run 'uloop compile' first."));
        }

        /// <summary>
        /// A path that Unity resolves to a compiled assembly is deferred to new-source
        /// membership validation instead of being rejected only because its source list is stale.
        /// </summary>
        [Test]
        public void TryGetAssemblyResolutionFailureReason_WhenSourceFilesDoNotContainPath_ReturnsNull()
        {
            UnityCompilationAssembly compilationAssembly = FindCompilationAssembly(HotReloadTestAssemblyName);
            Assert.That(compilationAssembly, Is.Not.Null);

            string reason = HotReloadAssemblyResolutionDiagnostics.TryGetAssemblyResolutionFailureReason(
                HotReloadTestAssemblyName,
                compilationAssembly,
                MissingHotReloadScriptPath,
                false);

            Assert.That(reason, Is.Null);
        }

        /// <summary>
        /// What: a script that already belongs to the compiled assembly is not treated as a
        /// resolution failure, so later hot-reload stages can still run.
        /// </summary>
        [Test]
        public void TryGetAssemblyResolutionFailureReason_WhenSourceFilesContainPath_ReturnsNull()
        {
            UnityCompilationAssembly compilationAssembly = FindCompilationAssembly(HotReloadTestAssemblyName);
            Assert.That(compilationAssembly, Is.Not.Null);

            string reason = HotReloadAssemblyResolutionDiagnostics.TryGetAssemblyResolutionFailureReason(
                HotReloadTestAssemblyName,
                compilationAssembly,
                ExistingHotReloadScriptPath,
                false);

            Assert.That(reason, Is.Null);
        }

        /// <summary>
        /// What: an on-disk .asmdef in the script's ancestor directory is detected without
        /// creating or importing any new assets.
        /// </summary>
        [Test]
        public void AncestorDirectoryContainsAsmdef_WhenAncestorHasAsmdef_ReturnsTrue()
        {
            Assert.That(
                HotReloadAssemblyResolutionDiagnostics.AncestorDirectoryContainsAsmdef(
                    "Assets/Tests/Editor/HotReload/NotExist.cs"),
                Is.True);
        }

        /// <summary>
        /// What: a loose Assets path whose ancestor directories have no .asmdef on disk
        /// is not reported as an unimported-asmdef case.
        /// </summary>
        [Test]
        public void AncestorDirectoryContainsAsmdef_WhenNoAncestorHasAsmdef_ReturnsFalse()
        {
            Assert.That(
                HotReloadAssemblyResolutionDiagnostics.AncestorDirectoryContainsAsmdef(
                    "Assets/RegressionHarness/HotReload/NotExist.cs"),
                Is.False);
        }

        /// <summary>
        /// What: sourceFiles that use Windows backslash separators still match the
        /// project-relative path after separator normalization.
        /// </summary>
        [Test]
        public void TryGetAssemblyResolutionFailureReason_WhenSourceFilesUseBackslashSeparators_ReturnsNull()
        {
            UnityCompilationAssembly compilationAssembly = new UnityCompilationAssembly(
                HotReloadTestAssemblyName,
                "Library/ScriptAssemblies/UnityCLILoop.Tests.Editor.HotReload.dll",
                new[] { @"Assets\Tests\Editor\HotReload\HotReloadToolTests.cs" },
                Array.Empty<string>(),
                Array.Empty<UnityCompilationAssembly>(),
                Array.Empty<string>(),
                AssemblyFlags.None);

            string reason = HotReloadAssemblyResolutionDiagnostics.TryGetAssemblyResolutionFailureReason(
                HotReloadTestAssemblyName,
                compilationAssembly,
                ExistingHotReloadScriptPath,
                false);

            Assert.That(reason, Is.Null);
        }

        /// <summary>
        /// ResolvePatchTarget admits a new source when the imported assembly definition
        /// still matches its disk boundary and compiled assembly.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenScriptIsNotInCompiledAssemblySourceFiles_ReturnsMembershipEvidence()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            (HotReloadFileProcessResult earlyResult,
                string projectRelativePath,
                string assemblyName,
                UnityCompilationAssembly compilationAssembly,
                string targetDllPath,
                string projectRoot,
                HotReloadUnchangedSourceDecision unchangedDecision,
                HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence) = HotReloadPatchTargetSupport.ResolvePatchTarget(
                MissingHotReloadScriptPath,
                MissingHotReloadScriptPath,
                outcomes,
                warnings,
                "assembly-resolution-wiring",
                new List<HotReloadMethodOutcome>());

            Assert.That(earlyResult, Is.Null);
            Assert.That(projectRelativePath, Is.EqualTo(MissingHotReloadScriptPath));
            Assert.That(assemblyName, Is.EqualTo(HotReloadTestAssemblyName));
            Assert.That(compilationAssembly, Is.Not.Null);
            Assert.That(targetDllPath, Is.Not.Null.And.Not.Empty);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            Assert.That(unchangedDecision, Is.EqualTo(HotReloadUnchangedSourceDecision.NotUnchanged));
            Assert.That(newSourceMembershipEvidence, Is.Not.Null);
        }

        private static UnityCompilationAssembly FindCompilationAssembly(string assemblyName)
        {
            UnityCompilationAssembly[] assemblies = CompilationPipeline.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                UnityCompilationAssembly assembly = assemblies[index];
                if (assembly.name == assemblyName)
                {
                    return assembly;
                }
            }

            return null;
        }
    }
}
