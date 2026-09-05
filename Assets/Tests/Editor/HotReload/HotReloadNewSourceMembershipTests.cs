using System;
using System.Collections.Generic;
using System.Text;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies that new-source membership admission stops before group processing while Editor state is unsafe.
    /// </summary>
    public sealed class HotReloadNewSourceMembershipTests
    {
        private const string MissingHotReloadScriptPath =
            "Assets/Tests/Editor/HotReload/UncompiledNewScript.cs";
        private const string MissingPredefinedScriptPath =
            "Assets/Util/UncompiledNewPredefinedScript.cs";

        private Func<HotReloadEditorStateSnapshot> _previousSnapshotProvider;

        [SetUp]
        public void SetUp()
        {
            _previousSnapshotProvider = HotReloadEditorStateSnapshotProvider.CaptureForTesting;
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = _previousSnapshotProvider;
        }

        /// <summary>
        /// Compiling, importing, and compilation-failed Editor states each stop the production target resolver before a group can be planned.
        /// </summary>
        [TestCase(true, false, false)]
        [TestCase(false, true, false)]
        [TestCase(false, false, true)]
        public void ResolvePatchTarget_WhenEditorStateIsUnsafe_ReturnsEarlyResult(
            bool isCompiling,
            bool isUpdating,
            bool scriptCompilationFailed)
        {
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(isCompiling, isUpdating, scriptCompilationFailed);
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            (HotReloadFileProcessResult earlyResult,
                string projectRelativePath,
                string assemblyName,
                UnityEditor.Compilation.Assembly compilationAssembly,
                string targetDllPath,
                string projectRoot,
                HotReloadUnchangedSourceDecision unchangedDecision,
                HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence) = HotReloadPatchTargetSupport.ResolvePatchTarget(
                MissingHotReloadScriptPath,
                MissingHotReloadScriptPath,
                outcomes,
                warnings,
                "new-source-editor-state",
                new List<HotReloadMethodOutcome>());

            Assert.That(earlyResult, Is.Not.Null);
            Assert.That(earlyResult.Outcomes, Has.Count.EqualTo(1));
            Assert.That(earlyResult.Outcomes[0].Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
            Assert.That(earlyResult.Outcomes[0].Reason, Does.Contain("retry hot reload"));
            Assert.That(projectRelativePath, Is.Null);
            Assert.That(assemblyName, Is.Null);
            Assert.That(compilationAssembly, Is.Null);
            Assert.That(targetDllPath, Is.Null);
            Assert.That(projectRoot, Is.Null);
            Assert.That(unchangedDecision, Is.EqualTo(HotReloadUnchangedSourceDecision.NotUnchanged));
            Assert.That(newSourceMembershipEvidence, Is.Null);
        }

        /// <summary>
        /// A ready Editor state admits the new source through the production resolver with membership evidence.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenEditorStateIsReady_ReturnsMembershipEvidence()
        {
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(false, false, false);
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            (HotReloadFileProcessResult earlyResult,
                string projectRelativePath,
                string assemblyName,
                UnityEditor.Compilation.Assembly compilationAssembly,
                string targetDllPath,
                string projectRoot,
                HotReloadUnchangedSourceDecision unchangedDecision,
                HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence) = HotReloadPatchTargetSupport.ResolvePatchTarget(
                MissingHotReloadScriptPath,
                MissingHotReloadScriptPath,
                outcomes,
                warnings,
                "new-source-editor-ready",
                new List<HotReloadMethodOutcome>());

            Assert.That(earlyResult, Is.Null);
            Assert.That(projectRelativePath, Is.EqualTo(MissingHotReloadScriptPath));
            Assert.That(assemblyName, Is.Not.Null.And.Not.Empty);
            Assert.That(compilationAssembly, Is.Not.Null);
            Assert.That(targetDllPath, Is.Not.Null.And.Not.Empty);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            Assert.That(unchangedDecision, Is.EqualTo(HotReloadUnchangedSourceDecision.NotUnchanged));
            Assert.That(newSourceMembershipEvidence, Is.Not.Null);
        }

        /// <summary>
        /// Disk bytes, imported bytes, and GUIDs are each part of the immutable membership evidence.
        /// </summary>
        [Test]
        public void EvidenceMatches_WhenBoundaryContentOrGuidChanges_ReturnsFalse()
        {
            HotReloadNewSourceMembershipEvidence baseline = CreateEvidence("disk", "import", "guid");
            HotReloadNewSourceMembershipEvidence diskChanged = CreateEvidence("next-disk", "import", "guid");
            HotReloadNewSourceMembershipEvidence importChanged = CreateEvidence("disk", "next-import", "guid");
            HotReloadNewSourceMembershipEvidence guidChanged = CreateEvidence("disk", "import", "next-guid");
            HotReloadNewSourceMembershipEvidence referencedTargetGuidChanged = CreateEvidence(
                "disk",
                "import",
                "guid",
                "next-target-guid");

            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, diskChanged), Is.False);
            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, importChanged), Is.False);
            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, guidChanged), Is.False);
            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, referencedTargetGuidChanged), Is.False);
        }

        /// <summary>
        /// A missing script outside every asmdef is admitted through the production predefined-assembly route with empty boundaries.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenNewPredefinedSourceIsReady_ReturnsMembershipEvidence()
        {
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(false, false, false);
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            List<string> warnings = new List<string>();

            (HotReloadFileProcessResult earlyResult,
                string projectRelativePath,
                string assemblyName,
                UnityEditor.Compilation.Assembly compilationAssembly,
                string targetDllPath,
                string projectRoot,
                HotReloadUnchangedSourceDecision unchangedDecision,
                HotReloadNewSourceMembershipEvidence newSourceMembershipEvidence) = HotReloadPatchTargetSupport.ResolvePatchTarget(
                MissingPredefinedScriptPath,
                MissingPredefinedScriptPath,
                outcomes,
                warnings,
                "new-predefined-source",
                new List<HotReloadMethodOutcome>());

            Assert.That(earlyResult, Is.Null);
            Assert.That(projectRelativePath, Is.EqualTo(MissingPredefinedScriptPath));
            Assert.That(assemblyName, Is.EqualTo("Assembly-CSharp"));
            Assert.That(compilationAssembly, Is.Not.Null);
            Assert.That(targetDllPath, Is.Not.Null.And.Not.Empty);
            Assert.That(projectRoot, Is.Not.Null.And.Not.Empty);
            Assert.That(unchangedDecision, Is.EqualTo(HotReloadUnchangedSourceDecision.NotUnchanged));
            Assert.That(newSourceMembershipEvidence, Is.Not.Null);
            Assert.That(newSourceMembershipEvidence.Boundaries, Is.Empty);
            Assert.That(newSourceMembershipEvidence.ResolvedAssemblyDefinitionPath, Is.Null);
        }

        /// <summary>
        /// Windows separator normalization keeps otherwise identical new-source evidence equal.
        /// </summary>
        [Test]
        public void EvidenceMatches_WhenSourcePathUsesWindowsSeparators_ReturnsTrue()
        {
            HotReloadNewSourceMembershipEvidence forwardSlash = CreateEvidence("disk", "import", "guid");
            HotReloadNewSourceMembershipEvidence backslash = new HotReloadNewSourceMembershipEvidence(
                "Assets\\Tests\\Editor\\HotReload\\UncompiledNewScript.cs",
                "TestAssembly",
                "Library/ScriptAssemblies/TestAssembly.dll",
                "mvid",
                "Assets\\Tests\\Editor\\HotReload\\Test.asmdef",
                new[]
                {
                    new HotReloadNewSourceMembershipBoundary(
                        "Assets\\Tests\\Editor\\HotReload\\Test.asmref",
                        "disk",
                        "import",
                        "guid",
                        "guid"),
                    new HotReloadNewSourceMembershipBoundary(
                        "Assets\\Definitions\\ReferencedTarget.asmdef",
                        "target-disk",
                        "target-import",
                        "target-guid",
                        "target-guid")
                });

            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(forwardSlash, backslash), Is.True);
        }

        /// <summary>
        /// An asmref resolves a differently named asmdef file through the JSON assembly name despite JSON whitespace.
        /// </summary>
        [Test]
        public void ResolveNearestBoundaryTarget_WhenAsmrefUsesAssemblyName_ReturnsDifferentlyNamedAsmdefPath()
        {
            string asmdefContents = "{\n  \"name\" : \"Declared Assembly\"\n}";
            HotReloadNewSourceMembershipBoundary asmref = new HotReloadNewSourceMembershipBoundary(
                "Assets/Feature/Nearest.asmref",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{ \"reference\" : \"Declared Assembly\" }")),
                "import",
                "guid",
                "guid");

            string resolvedPath = HotReloadNewSourceMembershipValidator.ResolveNearestBoundaryTarget(
                new[] { asmref },
                reference => null,
                reference => string.Equals(reference, HotReloadNewSourceMembershipValidator.ReadAssemblyDefinitionName(asmdefContents), StringComparison.Ordinal)
                    ? "Assets/Definitions/FileNameDoesNotMatch.asmdef"
                    : null);

            Assert.That(HotReloadNewSourceMembershipValidator.ReadAssemblyDefinitionName(asmdefContents), Is.EqualTo("Declared Assembly"));
            Assert.That(resolvedPath, Is.EqualTo("Assets/Definitions/FileNameDoesNotMatch.asmdef"));
        }

        /// <summary>
        /// The nearest asmdef wins before an outer asmref can resolve a different target.
        /// </summary>
        [Test]
        public void ResolveNearestBoundaryTarget_WhenNearestBoundaryIsAsmdef_IgnoresOuterAsmref()
        {
            HotReloadNewSourceMembershipBoundary nearestAsmdef = new HotReloadNewSourceMembershipBoundary(
                "Assets/Feature/Child/Nearest.asmdef",
                "disk",
                "import",
                "guid",
                "guid");
            HotReloadNewSourceMembershipBoundary outerAsmref = new HotReloadNewSourceMembershipBoundary(
                "Assets/Feature/Outer.asmref",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"reference\":\"OuterAssembly\"}")),
                "import",
                "guid",
                "guid");

            string resolvedPath = HotReloadNewSourceMembershipValidator.ResolveNearestBoundaryTarget(
                new[] { nearestAsmdef, outerAsmref },
                reference => "Assets/Definitions/Unexpected.asmdef",
                reference => "Assets/Definitions/Unexpected.asmdef");

            Assert.That(resolvedPath, Is.EqualTo("Assets/Feature/Child/Nearest.asmdef"));
        }

        /// <summary>
        /// The collector does not resolve an outer asmref target when an inner asmdef is nearest.
        /// </summary>
        [Test]
        public void ResolveNearestAsmrefTargetPath_WhenNearestBoundaryIsAsmdef_DoesNotResolveOuterAsmref()
        {
            List<string> boundaryPaths = new List<string>
            {
                "Assets/Feature/Child/Nearest.asmdef",
                "Assets/Feature/Outer.asmref"
            };
            int resolutionCalls = 0;

            string resolvedPath = HotReloadNewSourceMembershipBoundaryCollector.ResolveNearestAsmrefTargetPath(
                boundaryPaths,
                path =>
                {
                    resolutionCalls++;
                    return "Assets/Definitions/Unexpected.asmdef";
                });

            Assert.That(resolvedPath, Is.Null);
            Assert.That(resolutionCalls, Is.EqualTo(0));
        }

        /// <summary>
        /// The collector resolves the target only when the nearest boundary is an asmref.
        /// </summary>
        [Test]
        public void ResolveNearestAsmrefTargetPath_WhenNearestBoundaryIsAsmref_ResolvesItsTarget()
        {
            List<string> boundaryPaths = new List<string>
            {
                "Assets/Feature/Child/Nearest.asmref",
                "Assets/Feature/Outer.asmdef"
            };
            string resolvedFromPath = null;

            string resolvedPath = HotReloadNewSourceMembershipBoundaryCollector.ResolveNearestAsmrefTargetPath(
                boundaryPaths,
                path =>
                {
                    resolvedFromPath = path;
                    return "Assets/Definitions/NearestTarget.asmdef";
                });

            Assert.That(resolvedFromPath, Is.EqualTo("Assets/Feature/Child/Nearest.asmref"));
            Assert.That(resolvedPath, Is.EqualTo("Assets/Definitions/NearestTarget.asmdef"));
        }

        /// <summary>
        /// A disk-only boundary rejects a new assembly definition that Unity has not imported.
        /// </summary>
        [Test]
        public void TryCreateBoundary_WhenBoundaryIsNotImported_ReturnsFailure()
        {
            string failure = HotReloadNewSourceMembershipBoundaryCollector.TryCreateBoundary(
                "Assets/Feature/New.asmdef",
                Encoding.UTF8.GetBytes("disk"),
                null,
                "guid",
                "guid",
                out HotReloadNewSourceMembershipBoundary boundary);

            Assert.That(failure, Does.Contain("not imported"));
            Assert.That(boundary, Is.Null);
        }

        /// <summary>
        /// An imported boundary rejects deletion of its disk file.
        /// </summary>
        [Test]
        public void TryCreateBoundary_WhenDiskFileWasDeleted_ReturnsFailure()
        {
            string failure = HotReloadNewSourceMembershipBoundaryCollector.TryCreateBoundary(
                "Assets/Feature/Deleted.asmdef",
                null,
                Encoding.UTF8.GetBytes("import"),
                "guid",
                "guid",
                out HotReloadNewSourceMembershipBoundary boundary);

            Assert.That(failure, Does.Contain("deleted"));
            Assert.That(boundary, Is.Null);
        }

        /// <summary>
        /// Changed disk bytes and a changed imported GUID each invalidate a captured boundary.
        /// </summary>
        [Test]
        public void TryCreateBoundary_WhenContentsOrGuidChanges_ReturnsFailure()
        {
            string contentFailure = HotReloadNewSourceMembershipBoundaryCollector.TryCreateBoundary(
                "Assets/Feature/Changed.asmdef",
                Encoding.UTF8.GetBytes("disk"),
                Encoding.UTF8.GetBytes("import"),
                "guid",
                "guid",
                out HotReloadNewSourceMembershipBoundary contentBoundary);
            string guidFailure = HotReloadNewSourceMembershipBoundaryCollector.TryCreateBoundary(
                "Assets/Feature/Changed.asmdef",
                Encoding.UTF8.GetBytes("content"),
                Encoding.UTF8.GetBytes("content"),
                "disk-guid",
                "import-guid",
                out HotReloadNewSourceMembershipBoundary guidBoundary);

            Assert.That(contentFailure, Does.Contain("changed on disk"));
            Assert.That(guidFailure, Does.Contain("GUID differs"));
            Assert.That(contentBoundary, Is.Null);
            Assert.That(guidBoundary, Is.Null);
        }

        /// <summary>
        /// Matching disk and imported boundary data creates evidence for the production capture path.
        /// </summary>
        [Test]
        public void TryCreateBoundary_WhenDiskImportAndGuidMatch_ReturnsBoundary()
        {
            string failure = HotReloadNewSourceMembershipBoundaryCollector.TryCreateBoundary(
                "Assets/Feature/Ready.asmdef",
                Encoding.UTF8.GetBytes("content"),
                Encoding.UTF8.GetBytes("content"),
                "guid",
                "guid",
                out HotReloadNewSourceMembershipBoundary boundary);

            Assert.That(failure, Is.Null);
            Assert.That(boundary, Is.Not.Null);
            Assert.That(boundary.ProjectRelativePath, Is.EqualTo("Assets/Feature/Ready.asmdef"));
        }

        private static HotReloadNewSourceMembershipEvidence CreateEvidence(
            string diskContents,
            string importedContents,
            string guid,
            string referencedTargetGuid = "target-guid")
        {
            return new HotReloadNewSourceMembershipEvidence(
                "Assets/Tests/Editor/HotReload/UncompiledNewScript.cs",
                "TestAssembly",
                "Library/ScriptAssemblies/TestAssembly.dll",
                "mvid",
                "Assets/Tests/Editor/HotReload/Test.asmdef",
                new[]
                {
                    new HotReloadNewSourceMembershipBoundary(
                        "Assets/Tests/Editor/HotReload/Test.asmref",
                        diskContents,
                        importedContents,
                        guid,
                        guid),
                    new HotReloadNewSourceMembershipBoundary(
                        "Assets/Definitions/ReferencedTarget.asmdef",
                        "target-disk",
                        "target-import",
                        referencedTargetGuid,
                        referencedTargetGuid)
                });
        }
    }
}
