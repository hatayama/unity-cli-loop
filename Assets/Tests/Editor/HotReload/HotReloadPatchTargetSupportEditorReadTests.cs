using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the early exits of the patch target resolver for scripts that never exist on disk,
    /// reading only the real compilation pipeline and this project's folder layout.
    /// </summary>
    public sealed class HotReloadPatchTargetSupportEditorReadTests
    {
        /// <summary>
        /// Verifies that a script outside the project is refused as part of no compiled assembly, naming the path.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenScriptIsOutsideTheProject_ReturnsNotPartOfAnyAssemblyFailure()
        {
            // Never created: the path only has to lie outside the project root.
            string outsidePath = Path.Combine(
                Path.GetTempPath(),
                "uloop-test-" + Guid.NewGuid().ToString("N"),
                "Outside.cs");

            HotReloadPatchTargetResolution resolution = ResolveWithReadyEditor(outsidePath);

            AssertSingleFailure(
                resolution,
                "Script path is not part of any compiled assembly (Assets/Packages paths only): " + outsidePath);
        }

        /// <summary>
        /// Verifies that a script mapped to a predefined assembly with no compiled scripts is refused with the predefined-assembly reason.
        /// This depends on the project having no Assets/Plugins scripts, so Assembly-CSharp-firstpass is never compiled.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenPredefinedAssemblyHasNoCompiledScripts_ReturnsPredefinedNotCompiledFailure()
        {
            string pluginsPath = "Assets/Plugins/UloopMissing" + Guid.NewGuid().ToString("N") + "/NewSource.cs";
            Assert.That(
                CompilationPipeline.GetAssemblyNameFromScriptPath(pluginsPath),
                Is.EqualTo("Assembly-CSharp-firstpass.dll"),
                "Precondition: Unity maps Assets/Plugins to the firstpass assembly.");
            Assert.That(
                ContainsCompilationAssembly("Assembly-CSharp-firstpass"),
                Is.False,
                "Precondition: the project compiles no firstpass scripts.");

            HotReloadPatchTargetResolution resolution = ResolveWithReadyEditor(pluginsPath);

            AssertSingleFailure(
                resolution,
                string.Format(HotReloadConstants.PredefinedAssemblyNotCompiledReasonFormat, "Assembly-CSharp-firstpass"));
        }

        /// <summary>
        /// Verifies that a new source in a compiled assembly is refused with the membership failure when its directory does not exist.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenNewSourceDirectoryIsMissing_ReturnsMembershipFailure()
        {
            string missingDirectorySource =
                "Assets/Tests/Editor/HotReload/UloopMissing" + Guid.NewGuid().ToString("N") + "/NewSource.cs";

            HotReloadPatchTargetResolution resolution = ResolveWithReadyEditor(missingDirectorySource);

            AssertSingleFailure(
                resolution,
                "The new source membership boundary is not available on disk. Compile the project and retry hot reload.");
        }

        private static HotReloadPatchTargetResolution ResolveWithReadyEditor(string assemblyResolvePath)
        {
            return HotReloadPatchTargetSupport.ResolvePatchTarget(
                HotReloadCompositionRoot.Services.Domain,
                new EmptyPackageRootCapture(),
                new HotReloadStubEditorStateSnapshotCapture(() => new HotReloadEditorStateSnapshot(false, false, false)),
                assemblyResolvePath,
                assemblyResolvePath,
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                "patch-target-additions",
                new List<HotReloadMethodOutcome>());
        }

        private static void AssertSingleFailure(HotReloadPatchTargetResolution resolution, string expectedReason)
        {
            Assert.That(resolution.EarlyResult, Is.Not.Null);
            Assert.That(resolution.EarlyResult.Outcomes, Has.Count.EqualTo(1));
            Assert.That(resolution.EarlyResult.Outcomes[0].Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
            Assert.That(resolution.EarlyResult.Outcomes[0].Reason, Is.EqualTo(expectedReason));
            Assert.That(
                resolution.EarlyResult.Outcomes[0].FailureKinds,
                Is.EqualTo(HotReloadFailureKinds.Declaration),
                "None of these exits clears by waiting, so each keeps the fix advice.");
            Assert.That(resolution.NewSourceMembershipEvidence, Is.Null);
        }

        private static bool ContainsCompilationAssembly(string assemblyName)
        {
            foreach (UnityCompilationAssembly assembly in CompilationPipeline.GetAssemblies())
            {
                if (string.Equals(assembly.name, assemblyName, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// No package mapping, so path normalization reads no global capture; the tests use Assets and outside paths only.
        /// </summary>
        private sealed class EmptyPackageRootCapture : IHotReloadPackageRootCapture
        {
            public void CaptureCurrent()
            {
            }

            public IReadOnlyList<ScriptPackageRoot> Current => Array.Empty<ScriptPackageRoot>();
        }
    }
}
