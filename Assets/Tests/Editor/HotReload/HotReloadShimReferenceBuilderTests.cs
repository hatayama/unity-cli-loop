using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using NUnit.Framework;

using UnityEditor.Compilation;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the reference-list helpers of <see cref="HotReloadShimReferenceBuilder"/>
    /// that do not need a project root or a publicized copy.
    /// </summary>
    public sealed class HotReloadShimReferenceBuilderTests
    {
        private string _tempRoot;

        [SetUp]
        public void SetUp()
        {
            _tempRoot = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempRoot))
            {
                Directory.Delete(_tempRoot, true);
            }
        }

        /// <summary>
        /// Verifies that the target assembly is listed once when Unity's references already contain it.
        /// </summary>
        [Test]
        public void BuildWorkerReferencePaths_WhenReferencesAlreadyContainTarget_ListsTargetOnce()
        {
            string otherPath = Path.Combine(_tempRoot, "Fixture.Other.dll");
            string targetPath = Path.Combine(_tempRoot, "Fixture.Target.dll");
            File.WriteAllBytes(otherPath, new byte[] { 0 });
            File.WriteAllBytes(targetPath, new byte[] { 0 });
            UnityCompilationAssembly compilationAssembly = new UnityCompilationAssembly(
                "Fixture.Target",
                targetPath,
                Array.Empty<string>(),
                Array.Empty<string>(),
                Array.Empty<UnityCompilationAssembly>(),
                new[] { otherPath, targetPath },
                AssemblyFlags.None);
            HotReloadTypeHome targetHome = HotReloadTypeHome.ScriptAssemblies("Fixture.Target", targetPath);

            string[] paths = HotReloadShimReferenceBuilder.BuildWorkerReferencePaths(compilationAssembly, targetHome);

            Assert.That(paths, Is.EqualTo(new[] { Path.GetFullPath(otherPath), Path.GetFullPath(targetPath) }));
        }

        /// <summary>
        /// Verifies that a null home and a home whose DLL does not exist add no publicized reference.
        /// </summary>
        [Test]
        public void AppendPublicizedIntroducedTypeArtifactReferences_NullOrMissingHome_LeavesReferencesUnchanged()
        {
            List<string> references = new List<string> { "Existing.dll" };
            HotReloadTypeHome missingHome = HotReloadTypeHome.ScriptAssemblies(
                "Fixture.Missing",
                Path.Combine(_tempRoot, "Fixture.Missing.dll"));

            HotReloadShimReferenceBuilder.AppendPublicizedIntroducedTypeArtifactReferences(
                references,
                new[] { null, missingHome },
                Array.Empty<string>());

            Assert.That(references, Is.EqualTo(new[] { "Existing.dll" }));
        }

        /// <summary>
        /// Verifies that null homes are skipped while the other homes' directories join the resolver directories.
        /// </summary>
        [Test]
        public void CollectArtifactSearchDirectories_WithNullHome_AddsOnlyDirectoriesOfRealHomes()
        {
            string artifactDirectory = Path.Combine(_tempRoot, "artifacts");
            HotReloadTypeHome home = HotReloadTypeHome.ScriptAssemblies(
                "Fixture.Artifact",
                Path.Combine(artifactDirectory, "Fixture.Artifact.dll"));
            string resolverDirectory = Path.Combine(_tempRoot, "resolver");

            IReadOnlyCollection<string> directories = HotReloadShimReferenceBuilder.CollectArtifactSearchDirectories(
                new[] { null, home },
                new[] { resolverDirectory });

            Assert.That(
                directories.OrderBy(directory => directory, StringComparer.Ordinal).ToArray(),
                Is.EqualTo(new[] { Path.GetFullPath(artifactDirectory), resolverDirectory }
                    .OrderBy(directory => directory, StringComparer.Ordinal).ToArray()));
        }

        /// <summary>
        /// Verifies that null and empty entries are skipped when searching a reference list by full path.
        /// </summary>
        [Test]
        public void IndexOfFullPath_WithNullAndEmptyEntries_ReturnsIndexOfMatchingEntry()
        {
            string path = Path.GetFullPath(Path.Combine(_tempRoot, "Fixture.dll"));
            List<string> references = new List<string> { null, string.Empty, path };

            int index = HotReloadShimReferenceBuilder.IndexOfFullPath(references, path);

            Assert.That(index, Is.EqualTo(2));
        }

        /// <summary>
        /// Verifies that output without entries needs Harmony only when it has accessor delegates.
        /// </summary>
        [Test]
        public void NeedsHarmonyReference_EntriesOmitted_FollowsAccessorDelegateFlag()
        {
            TransformWorkerOutputDto withoutDelegates = new TransformWorkerOutputDto
            {
                entries = null,
                hasAccessorDelegates = false
            };
            TransformWorkerOutputDto withDelegates = new TransformWorkerOutputDto
            {
                entries = null,
                hasAccessorDelegates = true
            };

            Assert.That(HotReloadShimReferenceBuilder.NeedsHarmonyReference(withoutDelegates), Is.False);
            Assert.That(HotReloadShimReferenceBuilder.NeedsHarmonyReference(withDelegates), Is.True);
        }

        /// <summary>
        /// Verifies that a run with no retained artifact records and no prepared artifact resolves no homes.
        /// </summary>
        [Test]
        public void ResolveIntroducedTypeArtifactHomes_NoArtifactsAndNothingPrepared_ReturnsEmptyList()
        {
            HotReloadIntroducedTypeRegistry registry = new HotReloadIntroducedTypeRegistry();
            HotReloadDomain domain = new HotReloadDomain(
                registry,
                new HotReloadIntroducedTypeAssemblyResolver(registry));
            try
            {
                List<HotReloadTypeHome> homes = HotReloadShimReferenceBuilder.ResolveIntroducedTypeArtifactHomes(
                    domain,
                    _tempRoot,
                    null,
                    null);

                Assert.That(homes, Is.Empty);
            }
            finally
            {
                domain.Dispose();
            }
        }
    }
}
