using System;
using System.IO;
using System.Text;

using NUnit.Framework;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Covers the refusals of new-source membership capture and revalidation that depend on the real
    /// compilation pipeline and asset database, which are only read here.
    /// </summary>
    public sealed class HotReloadNewSourceMembershipValidatorEditorReadTests
    {
        private const string HotReloadTestAssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string EditorTestAssemblyName = "UnityCLILoop.Tests.Editor";
        private const string HotReloadTestAsmdefPath =
            "Assets/Tests/Editor/HotReload/UnityCLILoop.Tests.Editor.HotReload.asmdef";

        // The directory exists and belongs to the hot reload test assembly; the file is never created.
        private const string MissingSourceInExistingDirectory =
            "Assets/Tests/Editor/HotReload/UloopNeverCreatedMembershipSource.cs";

        private const string DifferentAssemblyFailure =
            "Unity resolved the new source to a different assembly. Compile the project and retry hot reload.";

        /// <summary>
        /// Verifies that capture refuses without evidence when Unity resolves the new source to a different assembly than the caller named.
        /// </summary>
        [Test]
        public void TryCapture_WhenUnityResolvesTheSourceToAnotherAssembly_ReturnsDifferentAssemblyFailure()
        {
            HotReloadFailureDescription failure = HotReloadNewSourceMembershipValidator.TryCapture(
                CreateReadyCapture(),
                GetProjectRoot(),
                MissingSourceInExistingDirectory,
                EditorTestAssemblyName,
                FindCompilationAssemblyByName(HotReloadTestAssemblyName),
                BuildScriptAssemblyDllPath(HotReloadTestAssemblyName),
                out HotReloadNewSourceMembershipEvidence evidence);

            Assert.That(failure?.Message, Is.EqualTo(DifferentAssemblyFailure));
            Assert.That(evidence, Is.Null);
        }

        /// <summary>
        /// Verifies that revalidation refuses when the evidence names an assembly the compilation pipeline no longer lists.
        /// </summary>
        [Test]
        public void TryRevalidate_WhenAssemblyIsNotInTheCompilationPipeline_ReturnsAssemblyGoneFailure()
        {
            string neverCompiledAssemblyName = "Uloop.NeverCompiled." + Guid.NewGuid().ToString("N");
            HotReloadNewSourceMembershipEvidence evidence = new HotReloadNewSourceMembershipEvidence(
                MissingSourceInExistingDirectory,
                neverCompiledAssemblyName,
                "Library/ScriptAssemblies/" + neverCompiledAssemblyName + ".dll",
                "mvid",
                null,
                Array.Empty<HotReloadNewSourceMembershipBoundary>());

            HotReloadFailureDescription failure = HotReloadNewSourceMembershipValidator.TryRevalidate(CreateReadyCapture(), evidence);

            Assert.That(
                failure?.Message,
                Is.EqualTo("The resolved assembly is no longer present in the compilation pipeline. Compile the project and retry hot reload."));
        }

        /// <summary>
        /// Verifies that revalidation of an unchanged compiled assembly still refuses when the new source's directory has disappeared.
        /// </summary>
        [Test]
        public void TryRevalidate_WhenSourceDirectoryIsMissing_ReturnsBoundaryFailure()
        {
            string missingDirectorySource =
                "Assets/Tests/Editor/HotReload/UloopMissing" + Guid.NewGuid().ToString("N") + "/NewSource.cs";
            HotReloadNewSourceMembershipEvidence evidence = CreateEvidenceForCompiledAssembly(
                missingDirectorySource,
                HotReloadTestAssemblyName,
                Array.Empty<HotReloadNewSourceMembershipBoundary>());

            HotReloadFailureDescription failure = HotReloadNewSourceMembershipValidator.TryRevalidate(CreateReadyCapture(), evidence);

            Assert.That(
                failure?.Message,
                Is.EqualTo("The new source membership boundary is not available on disk. Compile the project and retry hot reload."));
        }

        /// <summary>
        /// Verifies that revalidation refuses when the evidence names a compiled assembly other than the one Unity resolves the source to.
        /// </summary>
        [Test]
        public void TryRevalidate_WhenUnityResolvesTheSourceToAnotherAssembly_ReturnsDifferentAssemblyFailure()
        {
            HotReloadNewSourceMembershipEvidence evidence = CreateEvidenceForCompiledAssembly(
                MissingSourceInExistingDirectory,
                EditorTestAssemblyName,
                Array.Empty<HotReloadNewSourceMembershipBoundary>());

            HotReloadFailureDescription failure = HotReloadNewSourceMembershipValidator.TryRevalidate(CreateReadyCapture(), evidence);

            Assert.That(failure?.Message, Is.EqualTo(DifferentAssemblyFailure));
        }

        /// <summary>
        /// Verifies that revalidation refuses when the boundaries captured earlier no longer match the boundaries on disk.
        /// </summary>
        [Test]
        public void TryRevalidate_WhenCapturedBoundariesNoLongerMatch_ReturnsMembershipChangedFailure()
        {
            HotReloadFailureDescription captureFailure = HotReloadNewSourceMembershipValidator.TryCapture(
                CreateReadyCapture(),
                GetProjectRoot(),
                MissingSourceInExistingDirectory,
                HotReloadTestAssemblyName,
                FindCompilationAssemblyByName(HotReloadTestAssemblyName),
                BuildScriptAssemblyDllPath(HotReloadTestAssemblyName),
                out HotReloadNewSourceMembershipEvidence captured);
            Assert.That(captureFailure, Is.Null, "Precondition: the real capture must succeed.");
            Assert.That(captured.Boundaries.Length, Is.GreaterThan(0), "Precondition: the source sits under an asmdef.");
            HotReloadNewSourceMembershipEvidence withoutBoundaries = new HotReloadNewSourceMembershipEvidence(
                captured.ProjectRelativePath,
                captured.AssemblyName,
                captured.TargetDllPath,
                captured.TargetDllMvid,
                captured.ResolvedAssemblyDefinitionPath,
                Array.Empty<HotReloadNewSourceMembershipBoundary>());

            HotReloadFailureDescription failure = HotReloadNewSourceMembershipValidator.TryRevalidate(
                CreateReadyCapture(),
                withoutBoundaries);

            Assert.That(
                failure?.Message,
                Is.EqualTo("Assembly definition membership changed while hot reload was preparing. Compile the project and retry hot reload."));
        }

        /// <summary>
        /// Verifies that resolution refuses when the compilation assembly passed in is not the assembly the caller named.
        /// </summary>
        [Test]
        public void ValidateResolvedAssemblyDefinition_WhenCompilationAssemblyIsAnotherAssembly_ReturnsDifferentAssemblyFailure()
        {
            string failure = HotReloadNewSourceMembershipValidator.ValidateResolvedAssemblyDefinition(
                MissingSourceInExistingDirectory,
                HotReloadTestAssemblyName,
                FindCompilationAssemblyByName(EditorTestAssemblyName),
                Array.Empty<HotReloadNewSourceMembershipBoundary>(),
                null);

            Assert.That(failure, Is.EqualTo(DifferentAssemblyFailure));
        }

        /// <summary>
        /// Verifies that resolution refuses when the imported asmdef path differs from the one the boundaries lead to.
        /// </summary>
        [Test]
        public void ValidateResolvedAssemblyDefinition_WhenImportedAsmdefDiffersFromBoundary_ReturnsMismatchFailure()
        {
            string failure = HotReloadNewSourceMembershipValidator.ValidateResolvedAssemblyDefinition(
                MissingSourceInExistingDirectory,
                HotReloadTestAssemblyName,
                FindCompilationAssemblyByName(HotReloadTestAssemblyName),
                Array.Empty<HotReloadNewSourceMembershipBoundary>(),
                "Assets/UloopElsewhere/Other.asmdef");

            Assert.That(
                failure,
                Is.EqualTo("The imported assembly definition does not match the new source boundary. Compile the project and retry hot reload."));
        }

        /// <summary>
        /// Verifies that an asmref with an empty reference resolves to no target instead of asking either resolver.
        /// </summary>
        [Test]
        public void ResolveNearestBoundaryTarget_WhenAsmrefReferenceIsEmpty_ReturnsNull()
        {
            HotReloadNewSourceMembershipBoundary asmref = CreateAsmrefBoundary("{\"reference\":\"\"}");

            string resolvedPath = HotReloadNewSourceMembershipValidator.ResolveNearestBoundaryTarget(
                new[] { asmref },
                guid => "Assets/Unexpected/ByGuid.asmdef",
                name => "Assets/Unexpected/ByName.asmdef");

            Assert.That(resolvedPath, Is.Null);
        }

        /// <summary>
        /// Verifies that a GUID reference is resolved through the GUID resolver with the GUID prefix removed.
        /// </summary>
        [Test]
        public void ResolveNearestBoundaryTarget_WhenAsmrefUsesGuidReference_ResolvesTheBareGuid()
        {
            const string bareGuid = "0123456789abcdef0123456789abcdef";
            HotReloadNewSourceMembershipBoundary asmref =
                CreateAsmrefBoundary("{\"reference\":\"GUID:" + bareGuid + "\"}");

            string resolvedPath = HotReloadNewSourceMembershipValidator.ResolveNearestBoundaryTarget(
                new[] { asmref },
                guid => string.Equals(guid, bareGuid, StringComparison.Ordinal)
                    ? "Assets/Definitions/ByGuid.asmdef"
                    : null,
                name => "Assets/Unexpected/ByName.asmdef");

            Assert.That(resolvedPath, Is.EqualTo("Assets/Definitions/ByGuid.asmdef"));
        }

        /// <summary>
        /// Verifies that an imported asmdef is found by the assembly name its JSON declares.
        /// </summary>
        [Test]
        public void FindAsmdefPathByName_WhenAnImportedAsmdefDeclaresTheName_ReturnsItsAssetPath()
        {
            string assetPath = HotReloadNewSourceMembershipValidator.FindAsmdefPathByName(HotReloadTestAssemblyName);

            Assert.That(assetPath, Is.EqualTo(HotReloadTestAsmdefPath));
        }

        /// <summary>
        /// Verifies that evidence differing only in the assembly name does not match.
        /// </summary>
        [Test]
        public void EvidenceMatches_WhenOnlyAssemblyNameDiffers_ReturnsFalse()
        {
            HotReloadNewSourceMembershipBoundary[] boundaries = CreateSharedBoundaries();
            HotReloadNewSourceMembershipEvidence baseline = CreatePlainEvidence("Example.Assembly", "mvid", "Assets/Example/Example.asmdef", boundaries);
            HotReloadNewSourceMembershipEvidence same = CreatePlainEvidence("Example.Assembly", "mvid", "Assets/Example/Example.asmdef", boundaries);
            HotReloadNewSourceMembershipEvidence renamed = CreatePlainEvidence("Other.Assembly", "mvid", "Assets/Example/Example.asmdef", boundaries);

            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, same), Is.True, "Precondition: identical evidence must match.");
            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, renamed), Is.False);
        }

        /// <summary>
        /// Verifies that evidence differing only in the compiled assembly MVID does not match.
        /// </summary>
        [Test]
        public void EvidenceMatches_WhenOnlyTargetDllMvidDiffers_ReturnsFalse()
        {
            HotReloadNewSourceMembershipBoundary[] boundaries = CreateSharedBoundaries();
            HotReloadNewSourceMembershipEvidence baseline = CreatePlainEvidence("Example.Assembly", "mvid", "Assets/Example/Example.asmdef", boundaries);
            HotReloadNewSourceMembershipEvidence same = CreatePlainEvidence("Example.Assembly", "mvid", "Assets/Example/Example.asmdef", boundaries);
            HotReloadNewSourceMembershipEvidence recompiled = CreatePlainEvidence("Example.Assembly", "next-mvid", "Assets/Example/Example.asmdef", boundaries);

            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, same), Is.True, "Precondition: identical evidence must match.");
            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, recompiled), Is.False);
        }

        /// <summary>
        /// Verifies that evidence differing only in the resolved asmdef path does not match.
        /// </summary>
        [Test]
        public void EvidenceMatches_WhenOnlyResolvedAssemblyDefinitionPathDiffers_ReturnsFalse()
        {
            HotReloadNewSourceMembershipBoundary[] boundaries = CreateSharedBoundaries();
            HotReloadNewSourceMembershipEvidence baseline = CreatePlainEvidence("Example.Assembly", "mvid", "Assets/Example/Example.asmdef", boundaries);
            HotReloadNewSourceMembershipEvidence same = CreatePlainEvidence("Example.Assembly", "mvid", "Assets/Example/Example.asmdef", boundaries);
            HotReloadNewSourceMembershipEvidence moved = CreatePlainEvidence("Example.Assembly", "mvid", "Assets/Moved/Example.asmdef", boundaries);

            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, same), Is.True, "Precondition: identical evidence must match.");
            Assert.That(HotReloadNewSourceMembershipValidator.EvidenceMatches(baseline, moved), Is.False);
        }

        private static HotReloadStubEditorStateSnapshotCapture CreateReadyCapture()
        {
            return new HotReloadStubEditorStateSnapshotCapture(() => new HotReloadEditorStateSnapshot(false, false, false));
        }

        private static string GetProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(UnityEngine.Application.dataPath, ".."));
        }

        // Built exactly as revalidation builds it, so the dll-path check passes and the later branches are reached.
        private static string BuildScriptAssemblyDllPath(string assemblyName)
        {
            return Path.GetFullPath(Path.Combine(
                GetProjectRoot(),
                HotReloadConstants.ScriptAssembliesRelativeDirectory,
                assemblyName + HotReloadConstants.CompiledAssemblyExtension));
        }

        private static HotReloadNewSourceMembershipEvidence CreateEvidenceForCompiledAssembly(
            string projectRelativePath,
            string assemblyName,
            HotReloadNewSourceMembershipBoundary[] boundaries)
        {
            string dllPath = BuildScriptAssemblyDllPath(assemblyName);
            return new HotReloadNewSourceMembershipEvidence(
                projectRelativePath,
                assemblyName,
                dllPath,
                HotReloadSourceSnapshotter.ReadAssemblyMvid(dllPath),
                null,
                boundaries);
        }

        private static UnityCompilationAssembly FindCompilationAssemblyByName(string assemblyName)
        {
            UnityCompilationAssembly[] assemblies = CompilationPipeline.GetAssemblies();
            for (int index = 0; index < assemblies.Length; index++)
            {
                if (string.Equals(assemblies[index].name, assemblyName, StringComparison.Ordinal))
                {
                    return assemblies[index];
                }
            }

            Assert.Fail("The compilation assembly '" + assemblyName + "' was not found.");
            return null;
        }

        private static HotReloadNewSourceMembershipBoundary CreateAsmrefBoundary(string json)
        {
            return new HotReloadNewSourceMembershipBoundary(
                "Assets/Feature/Nearest.asmref",
                Convert.ToBase64String(Encoding.UTF8.GetBytes(json)),
                "import",
                "guid",
                "guid");
        }

        private static HotReloadNewSourceMembershipBoundary[] CreateSharedBoundaries()
        {
            return new[]
            {
                new HotReloadNewSourceMembershipBoundary(
                    "Assets/Example/Example.asmdef",
                    "disk",
                    "import",
                    "guid",
                    "guid")
            };
        }

        private static HotReloadNewSourceMembershipEvidence CreatePlainEvidence(
            string assemblyName,
            string mvid,
            string resolvedAssemblyDefinitionPath,
            HotReloadNewSourceMembershipBoundary[] boundaries)
        {
            return new HotReloadNewSourceMembershipEvidence(
                "Assets/Example/NewSource.cs",
                assemblyName,
                "Library/ScriptAssemblies/Example.Assembly.dll",
                mvid,
                resolvedAssemblyDefinitionPath,
                boundaries);
        }
    }
}
