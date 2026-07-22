using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies seed file paths resolve to a deduplicated set of assembly directories
    /// (used to scope the migration plan-building scan to only the assemblies that matter), and that
    /// seeds with no real assembly directory (implicit assemblies) are reported via a flag instead of
    /// being silently dropped as a nonexistent scan directory.
    /// </summary>
    public sealed class ThirdPartyToolMigrationScanScopeResolverTests
    {
        private const string ProjectRoot = "/Project";

        [Test]
        public void ResolveScopeAssemblyDirectories_WhenSeedFilesAreUnderSameAsmdefDirectory_ReturnsSingleDirectory()
        {
            // Verifies that multiple seed files under the same asmdef directory collapse to one scope entry.
            List<string> seedFilePaths = new List<string>
            {
                "/Project/Assets/ToolA/Editor/FileOne.cs",
                "/Project/Assets/ToolA/Editor/FileTwo.cs"
            };
            List<string> asmdefDirectories = new List<string> { "/Project/Assets/ToolA/Editor" };
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new List<AssemblyReferenceDirectory>();

            (List<string> AssemblyDirectories, bool HasImplicitAssemblySeeds) result =
                ThirdPartyToolMigrationScanScopeResolver.ResolveScopeAssemblyDirectories(
                    seedFilePaths,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    ProjectRoot);

            Assert.That(result.AssemblyDirectories, Is.EqualTo(new[] { "/Project/Assets/ToolA/Editor" }));
            Assert.That(result.HasImplicitAssemblySeeds, Is.False);
        }

        [Test]
        public void ResolveScopeAssemblyDirectories_WhenSeedFilesAreUnderDifferentAsmdefDirectories_ReturnsBothDirectories()
        {
            // Verifies that seed files spanning multiple assemblies produce one scope entry per assembly.
            List<string> seedFilePaths = new List<string>
            {
                "/Project/Assets/ToolA/Editor/FileOne.cs",
                "/Project/Assets/ToolB/Editor/FileTwo.cs"
            };
            List<string> asmdefDirectories = new List<string>
            {
                "/Project/Assets/ToolA/Editor",
                "/Project/Assets/ToolB/Editor"
            };
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new List<AssemblyReferenceDirectory>();

            (List<string> AssemblyDirectories, bool HasImplicitAssemblySeeds) result =
                ThirdPartyToolMigrationScanScopeResolver.ResolveScopeAssemblyDirectories(
                    seedFilePaths,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    ProjectRoot);

            Assert.That(
                result.AssemblyDirectories,
                Is.EqualTo(new[] { "/Project/Assets/ToolA/Editor", "/Project/Assets/ToolB/Editor" }));
            Assert.That(result.HasImplicitAssemblySeeds, Is.False);
        }

        [Test]
        public void ResolveScopeAssemblyDirectories_WhenSeedFileHasNoAsmdef_SetsImplicitAssemblySeedsFlagInsteadOfAScopeDirectory()
        {
            // Verifies that a seed file outside any asmdef directory resolves to the synthetic implicit
            // assembly marker (not a real directory on disk), so it must be reported via the
            // HasImplicitAssemblySeeds flag instead of being added to AssemblyDirectories — adding the
            // synthetic marker there would make the scoped scan silently skip it as "directory not found"
            // instead of falling back to a full scan.
            List<string> seedFilePaths = new List<string> { "/Project/Assets/Editor/LooseFile.cs" };
            List<string> asmdefDirectories = new List<string>();
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new List<AssemblyReferenceDirectory>();

            (List<string> AssemblyDirectories, bool HasImplicitAssemblySeeds) result =
                ThirdPartyToolMigrationScanScopeResolver.ResolveScopeAssemblyDirectories(
                    seedFilePaths,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    ProjectRoot);

            Assert.That(result.AssemblyDirectories, Is.Empty);
            Assert.That(result.HasImplicitAssemblySeeds, Is.True);
        }

        [Test]
        public void ResolveScopeAssemblyDirectories_WhenSeedFilesMixRealAsmdefAndImplicit_ReturnsRealDirectoryAndSetsFlag()
        {
            // Verifies that a mix of a real-asmdef seed and an implicit-assembly seed still reports the
            // real assembly directory while also setting HasImplicitAssemblySeeds, so a caller knows the
            // returned directory list alone is not a complete/safe scope.
            List<string> seedFilePaths = new List<string>
            {
                "/Project/Assets/ToolA/Editor/FileOne.cs",
                "/Project/Assets/Editor/LooseFile.cs"
            };
            List<string> asmdefDirectories = new List<string> { "/Project/Assets/ToolA/Editor" };
            List<AssemblyReferenceDirectory> assemblyReferenceDirectories = new List<AssemblyReferenceDirectory>();

            (List<string> AssemblyDirectories, bool HasImplicitAssemblySeeds) result =
                ThirdPartyToolMigrationScanScopeResolver.ResolveScopeAssemblyDirectories(
                    seedFilePaths,
                    asmdefDirectories,
                    assemblyReferenceDirectories,
                    ProjectRoot);

            Assert.That(result.AssemblyDirectories, Is.EqualTo(new[] { "/Project/Assets/ToolA/Editor" }));
            Assert.That(result.HasImplicitAssemblySeeds, Is.True);
        }

        [Test]
        public void ResolveScopeAssemblyDirectories_WhenSeedFilePathsIsEmpty_ReturnsEmptyListAndNoImplicitFlag()
        {
            // Verifies that no seed files produce no scope and no implicit-assembly flag, rather than
            // accidentally scoping to the whole project or forcing an unnecessary fallback.
            (List<string> AssemblyDirectories, bool HasImplicitAssemblySeeds) result =
                ThirdPartyToolMigrationScanScopeResolver.ResolveScopeAssemblyDirectories(
                    new List<string>(),
                    new List<string>(),
                    new List<AssemblyReferenceDirectory>(),
                    ProjectRoot);

            Assert.That(result.AssemblyDirectories, Is.Empty);
            Assert.That(result.HasImplicitAssemblySeeds, Is.False);
        }

        [Test]
        public void ResolveScopeAssemblyDirectories_WhenSeedFilePathsIsNull_Throws()
        {
            // Verifies fail-fast behavior when the seed file collection itself is missing.
            Assert.Throws<System.ArgumentNullException>(() =>
                ThirdPartyToolMigrationScanScopeResolver.ResolveScopeAssemblyDirectories(
                    null,
                    new List<string>(),
                    new List<AssemblyReferenceDirectory>(),
                    ProjectRoot));
        }
    }
}
