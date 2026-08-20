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
        /// What: a path that Unity resolves to a compiled assembly but that is not in that
        /// assembly's last compiled source list gets the newly-added-script reason.
        /// </summary>
        [Test]
        public void TryGetAssemblyResolutionFailureReason_WhenSourceFilesDoNotContainPath_ReturnsNewScriptReason()
        {
            UnityCompilationAssembly compilationAssembly = FindCompilationAssembly(HotReloadTestAssemblyName);
            Assert.That(compilationAssembly, Is.Not.Null);

            string reason = HotReloadAssemblyResolutionDiagnostics.TryGetAssemblyResolutionFailureReason(
                HotReloadTestAssemblyName,
                compilationAssembly,
                MissingHotReloadScriptPath,
                false);

            Assert.That(
                reason,
                Is.EqualTo(
                    "'Assets/Tests/Editor/HotReload/UncompiledNewScript.cs' is not part of the last compiled assembly 'UnityCLILoop.Tests.Editor.HotReload' (a newly added script). New files require a real compile; run 'uloop compile' first."));
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
        /// What: ResolvePatchTarget fails with the newly-added-script reason when
        /// CompilationPipeline maps a missing path onto an existing asmdef whose compiled
        /// source list does not contain that path.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenScriptIsNotInCompiledAssemblySourceFiles_ReturnsFailedReason()
        {
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            (HotReloadOrchestrator.HotReloadFileProcessResult earlyResult,
                string projectRelativePath,
                string assemblyName,
                UnityCompilationAssembly compilationAssembly,
                string targetDllPath,
                string projectRoot) = HotReloadPatchTargetSupport.ResolvePatchTarget(
                MissingHotReloadScriptPath,
                MissingHotReloadScriptPath,
                outcomes,
                warnings,
                "assembly-resolution-wiring");

            Assert.That(earlyResult, Is.Not.Null);
            Assert.That(earlyResult.Outcomes.Count, Is.EqualTo(1));
            Assert.That(earlyResult.Outcomes[0].Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
            Assert.That(
                earlyResult.Outcomes[0].Reason,
                Is.EqualTo(
                    "'Assets/Tests/Editor/HotReload/UncompiledNewScript.cs' is not part of the last compiled assembly 'UnityCLILoop.Tests.Editor.HotReload' (a newly added script). New files require a real compile; run 'uloop compile' first."));
            Assert.That(projectRelativePath, Is.Null);
            Assert.That(assemblyName, Is.Null);
            Assert.That(compilationAssembly, Is.Null);
            Assert.That(targetDllPath, Is.Null);
            Assert.That(projectRoot, Is.Null);
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
