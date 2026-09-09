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
        private const string ExistingScriptPath =
            "Assets/Tests/Editor/HotReload/HotReloadNewSourceMembershipTests.cs";

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

            HotReloadPatchTargetResolution resolution = HotReloadPatchTargetSupport.ResolvePatchTarget(
                MissingHotReloadScriptPath,
                MissingHotReloadScriptPath,
                outcomes,
                warnings,
                "new-source-editor-state",
                new List<HotReloadMethodOutcome>());

            Assert.That(resolution.EarlyResult, Is.Not.Null);
            Assert.That(resolution.EarlyResult.Outcomes, Has.Count.EqualTo(1));
            Assert.That(resolution.EarlyResult.Outcomes[0].Kind, Is.EqualTo(HotReloadMethodOutcomeKind.Failed));
            Assert.That(resolution.EarlyResult.Outcomes[0].Reason, Does.Contain("retry hot reload"));
            Assert.That(resolution.ProjectRelativePath, Is.Null);
            Assert.That(resolution.AssemblyName, Is.Null);
            Assert.That(resolution.CompilationAssembly, Is.Null);
            Assert.That(resolution.TargetDllPath, Is.Null);
            Assert.That(resolution.ProjectRoot, Is.Null);
            Assert.That(resolution.UnchangedDecision, Is.EqualTo(HotReloadUnchangedSourceDecision.NotUnchanged));
            Assert.That(resolution.NewSourceMembershipEvidence, Is.Null);
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

            HotReloadPatchTargetResolution resolution = HotReloadPatchTargetSupport.ResolvePatchTarget(
                MissingHotReloadScriptPath,
                MissingHotReloadScriptPath,
                outcomes,
                warnings,
                "new-source-editor-ready",
                new List<HotReloadMethodOutcome>());

            Assert.That(resolution.EarlyResult, Is.Null);
            Assert.That(resolution.ProjectRelativePath, Is.EqualTo(MissingHotReloadScriptPath));
            Assert.That(resolution.AssemblyName, Is.Not.Null.And.Not.Empty);
            Assert.That(resolution.CompilationAssembly, Is.Not.Null);
            Assert.That(resolution.TargetDllPath, Is.Not.Null.And.Not.Empty);
            Assert.That(resolution.ProjectRoot, Is.Not.Null.And.Not.Empty);
            Assert.That(resolution.UnchangedDecision, Is.EqualTo(HotReloadUnchangedSourceDecision.NotUnchanged));
            Assert.That(resolution.NewSourceMembershipEvidence, Is.Not.Null);
        }

        /// <summary>
        /// An assembly that still owns an introduced type keeps its applied-source ledger entry,
        /// because the unchanged-source short-circuit is not called for it at all.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenAssemblyOwnsAnActiveIntroducedType_KeepsTheLedgerEntry()
        {
            string existingScriptPath = ExistingScriptPath;
            HotReloadDomainSlot.Current.ClearAppliedSource(existingScriptPath);
            HotReloadDomainSlot.Current.RecordAppliedSource(existingScriptPath, "stale-hash", true);

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();
                ActivateIntroducedTypeFor(existingScriptPath);

                ResolveExistingScript("introduced-type-active");
            }

            Assert.That(HotReloadDomainSlot.Current.TryGetAppliedSource(existingScriptPath), Is.Not.Null);
            HotReloadDomainSlot.Current.ClearAppliedSource(existingScriptPath);
        }

        /// <summary>
        /// An assembly without an introduced type still runs the unchanged-source short-circuit,
        /// which clears a ledger entry that no longer matches the file.
        /// </summary>
        [Test]
        public void ResolvePatchTarget_WhenAssemblyOwnsNoIntroducedType_RunsTheShortCircuit()
        {
            string existingScriptPath = ExistingScriptPath;
            HotReloadDomainSlot.Current.ClearAppliedSource(existingScriptPath);
            HotReloadDomainSlot.Current.RecordAppliedSource(existingScriptPath, "stale-hash", true);

            using (HotReloadIntroducedTypeHolder.BeginReplacement())
            {
                HotReloadIntroducedTypeHolder.Initialize();

                ResolveExistingScript("introduced-type-absent");
            }

            Assert.That(HotReloadDomainSlot.Current.TryGetAppliedSource(existingScriptPath), Is.Null);
        }

        private static void ResolveExistingScript(string correlationId)
        {
            HotReloadEditorStateSnapshotProvider.CaptureForTesting = () =>
                new HotReloadEditorStateSnapshot(false, false, false);
            HotReloadPatchTargetSupport.ResolvePatchTarget(
                ExistingScriptPath,
                ExistingScriptPath,
                new List<HotReloadMethodOutcome>(),
                new List<string>(),
                correlationId,
                new List<HotReloadMethodOutcome>());
        }

        private static void ActivateIntroducedTypeFor(string projectRelativeScriptPath)
        {
            string assemblyName = System.IO.Path.GetFileNameWithoutExtension(
                UnityEditor.Compilation.CompilationPipeline.GetAssemblyNameFromScriptPath(projectRelativeScriptPath));
            HotReloadIntroducedTypeDescriptor descriptor = new HotReloadIntroducedTypeDescriptor(
                assemblyName,
                "original-mvid",
                "Example.Introduced",
                projectRelativeScriptPath,
                "fingerprint",
                "public class Introduced { }");
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadNewSourceMembershipTests).Assembly,
                "artifact.dll",
                "artifact.pdb",
                new List<HotReloadIntroducedTypeDescriptor> { descriptor });
            HotReloadIntroducedTypeHolder.Registry.RegisterPrepared(artifact);
            HotReloadIntroducedTypeHolder.Registry.Activate(artifact);
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

            HotReloadPatchTargetResolution resolution = HotReloadPatchTargetSupport.ResolvePatchTarget(
                MissingPredefinedScriptPath,
                MissingPredefinedScriptPath,
                outcomes,
                warnings,
                "new-predefined-source",
                new List<HotReloadMethodOutcome>());

            Assert.That(resolution.EarlyResult, Is.Null);
            Assert.That(resolution.ProjectRelativePath, Is.EqualTo(MissingPredefinedScriptPath));
            Assert.That(resolution.AssemblyName, Is.EqualTo("Assembly-CSharp"));
            Assert.That(resolution.CompilationAssembly, Is.Not.Null);
            Assert.That(resolution.TargetDllPath, Is.Not.Null.And.Not.Empty);
            Assert.That(resolution.ProjectRoot, Is.Not.Null.And.Not.Empty);
            Assert.That(resolution.UnchangedDecision, Is.EqualTo(HotReloadUnchangedSourceDecision.NotUnchanged));
            Assert.That(resolution.NewSourceMembershipEvidence, Is.Not.Null);
            Assert.That(resolution.NewSourceMembershipEvidence.Boundaries, Is.Empty);
            Assert.That(resolution.NewSourceMembershipEvidence.ResolvedAssemblyDefinitionPath, Is.Null);
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
        /// An unresolved nearest asmref target is rejected instead of being mistaken for a predefined assembly.
        /// </summary>
        [Test]
        public void TryResolveNearestBoundaryTarget_WhenAsmrefTargetIsMissing_ReturnsFailure()
        {
            HotReloadNewSourceMembershipBoundary asmref = new HotReloadNewSourceMembershipBoundary(
                "Assets/Feature/Nearest.asmref",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{\"reference\":\"MissingAssembly\"}")),
                "import",
                "guid",
                "guid");

            string failure = HotReloadNewSourceMembershipValidator.TryResolveNearestBoundaryTarget(
                new[] { asmref },
                reference => null,
                reference => null,
                out string targetPath);

            Assert.That(failure, Does.Contain("could not be resolved"));
            Assert.That(targetPath, Is.Null);
        }

        /// <summary>
        /// Malformed assembly boundary JSON is treated as unresolved by Unity's structured parser.
        /// </summary>
        [Test]
        public void ReadAssemblyBoundaryJson_WhenMalformed_ReturnsNull()
        {
            Assert.That(HotReloadNewSourceMembershipValidator.ReadAssemblyDefinitionName("{ invalid"), Is.Null);
            Assert.That(HotReloadNewSourceMembershipValidator.ReadAsmrefReference("{ invalid"), Is.Null);
        }

        /// <summary>
        /// Malformed asmdef and asmref boundary contents fail membership validation with retry guidance.
        /// </summary>
        [Test]
        public void TryValidateBoundaryJson_WhenAsmdefOrAsmrefIsMalformed_ReturnsFailure()
        {
            HotReloadNewSourceMembershipBoundary malformedAsmdef = new HotReloadNewSourceMembershipBoundary(
                "Assets/Feature/Invalid.asmdef",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{ invalid")),
                "import",
                "guid",
                "guid");
            HotReloadNewSourceMembershipBoundary malformedAsmref = new HotReloadNewSourceMembershipBoundary(
                "Assets/Feature/Invalid.asmref",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{ invalid")),
                "import",
                "guid",
                "guid");

            string asmdefFailure = HotReloadNewSourceMembershipValidator.TryValidateBoundaryJson(
                new[] { malformedAsmdef });
            string asmrefFailure = HotReloadNewSourceMembershipValidator.TryValidateBoundaryJson(
                new[] { malformedAsmref });

            Assert.That(asmdefFailure, Does.Contain("invalid JSON"));
            Assert.That(asmrefFailure, Does.Contain("invalid JSON"));
        }

        /// <summary>
        /// The production resolved-assembly boundary rejects malformed boundary JSON before admitting a new source.
        /// </summary>
        [Test]
        public void ValidateResolvedAssemblyDefinition_WhenBoundaryJsonIsMalformed_ReturnsFailure()
        {
            UnityEditor.Compilation.Assembly compilationAssembly = FindHotReloadCompilationAssembly();
            string resolvedAssemblyDefinitionPath = UnityEditor.Compilation.CompilationPipeline
                .GetAssemblyDefinitionFilePathFromScriptPath(MissingHotReloadScriptPath);
            HotReloadNewSourceMembershipBoundary malformedBoundary = new HotReloadNewSourceMembershipBoundary(
                resolvedAssemblyDefinitionPath,
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{ invalid")),
                "import",
                "guid",
                "guid");

            string failure = HotReloadNewSourceMembershipValidator.ValidateResolvedAssemblyDefinition(
                MissingHotReloadScriptPath,
                compilationAssembly.name,
                compilationAssembly,
                new[] { malformedBoundary },
                resolvedAssemblyDefinitionPath);

            Assert.That(failure, Does.Contain("invalid JSON"));
        }

        /// <summary>
        /// The production resolved-assembly boundary rejects an unresolved nearest asmref before admitting a new source.
        /// </summary>
        [Test]
        public void ValidateResolvedAssemblyDefinition_WhenNearestAsmrefIsUnresolved_ReturnsFailure()
        {
            UnityEditor.Compilation.Assembly compilationAssembly = FindHotReloadCompilationAssembly();
            string resolvedAssemblyDefinitionPath = UnityEditor.Compilation.CompilationPipeline
                .GetAssemblyDefinitionFilePathFromScriptPath(MissingHotReloadScriptPath);
            HotReloadNewSourceMembershipBoundary unresolvedAsmref = new HotReloadNewSourceMembershipBoundary(
                "Assets/Tests/Editor/HotReload/Unresolved.asmref",
                Convert.ToBase64String(Encoding.UTF8.GetBytes("{ \"reference\" : \"Missing Assembly\" }")),
                "import",
                "guid",
                "guid");

            string failure = HotReloadNewSourceMembershipValidator.ValidateResolvedAssemblyDefinition(
                MissingHotReloadScriptPath,
                compilationAssembly.name,
                compilationAssembly,
                new[] { unresolvedAsmref },
                resolvedAssemblyDefinitionPath);

            Assert.That(failure, Does.Contain("could not be resolved"));
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

        /// <summary>
        /// A package path without a readable physical directory fails membership capture before directory enumeration.
        /// </summary>
        [Test]
        public void TryCapture_WhenPackageBoundaryDirectoryIsUnavailable_ReturnsFailure()
        {
            string projectRoot = System.IO.Path.GetFullPath(
                System.IO.Path.Combine(UnityEngine.Application.dataPath, ".."));

            string failure = HotReloadNewSourceMembershipBoundaryCollector.TryCapture(
                projectRoot,
                "Packages/unavailable-package/NewSource.cs",
                out HotReloadNewSourceMembershipBoundary[] boundaries);

            Assert.That(failure, Does.Contain("not available on disk"));
            Assert.That(boundaries, Is.Null);
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

        private static UnityEditor.Compilation.Assembly FindHotReloadCompilationAssembly()
        {
            UnityEditor.Compilation.Assembly[] assemblies = UnityEditor.Compilation.CompilationPipeline.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                if (string.Equals(assemblies[index].name, "UnityCLILoop.Tests.Editor.HotReload", StringComparison.Ordinal))
                {
                    return assemblies[index];
                }
            }

            Assert.Fail("The hot reload test assembly was not found.");
            return null;
        }
    }
}
