using System.Collections.Generic;
using System.IO;
using System.Reflection;

using HarmonyLib;
using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

using UnityCompilationAssembly = UnityEditor.Compilation.Assembly;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the peel of the patches this run's worker reported as unchanged
    /// methods, which the commit stage runs for a group that commits introduced types.
    /// </summary>
    public class HotReloadUnchangedPatchPeelTests
    {
        private const string AssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string OwnerPath = "Assets/Tests/Editor/HotReload/HotReloadCoreFixtures.cs";
        private const string FixtureMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadCoreFixture";

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _scope = new HotReloadDomainTestScope();
        }

        [TearDown]
        public void TearDown()
        {
            _scope.Dispose();
        }

        /// <summary>
        /// What: a file the shim compile refused to apply keeps the patch the previous reload
        /// installed on a method this run reports unchanged, instead of having it peeled as stale.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_FileTheShimCompileRefusedToApply_KeepsThePreviousRunsPatch()
        {
            HotReloadGroupFile file = ArrangeUnchangedMethodWithActivePatch();
            file.SkipApply = true;

            Revert(file);

            Assert.That(
                file.RevertedUnchangedCount,
                Is.EqualTo(0),
                "A file this run does not apply must keep the previous run's patches, so the peel "
                + "of its unchanged methods has to be skipped along with the apply.");
            Assert.That(
                HotReloadCoreFixture.StaticPing(),
                Is.EqualTo("patched"),
                "The patch of the previous reload must still be live.");
        }

        /// <summary>
        /// What: a file this run does apply has the patch of the previous reload peeled off a
        /// method this run reports unchanged, so the compiled assembly runs its own code again.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_FileThisRunApplies_PeelsThePreviousRunsPatch()
        {
            HotReloadGroupFile file = ArrangeUnchangedMethodWithActivePatch();

            Revert(file);

            Assert.That(
                file.RevertedUnchangedCount,
                Is.EqualTo(1),
                "A method the worker reported unchanged has nothing left to patch, so the patch of "
                + "the previous reload is peeled.");
            Assert.That(
                HotReloadCoreFixture.StaticPing(),
                Is.EqualTo("original"),
                "The peeled method must run the code its own assembly holds again.");
        }

        private static void Revert(HotReloadGroupFile file)
        {
            HotReloadCompositionRoot.Services.EntryApplier.RevertUnchangedPatchesPerFile(
                new[] { file },
                HotReloadWorkerRowsByFile.Build(BuildWorkerOutput(), new[] { OwnerPath }));
        }

        private static HotReloadGroupFile ArrangeUnchangedMethodWithActivePatch()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.StaticPing));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims),
                nameof(HotReloadHandwrittenShims.StaticPing__shim0));
            Assert.That(
                HotReloadCoreFixture.StaticPing(),
                Is.EqualTo("original"),
                "Precondition: the fixture must start unpatched.");
            HotReloadPatchResult patch = new HotReloadDomainTestAccess().ApplyPatch(
                original, shim, HotReloadPatchShape.Transplant, OwnerPath);
            Assert.That(patch.Success, Is.True, patch.ErrorMessage);

            HotReloadFileSinks sinks = new HotReloadFileSinks(new List<string>(), null);
            return new HotReloadGroupFile(
                OwnerPath,
                OwnerPath,
                OwnerPath,
                AssemblyName,
                FindCompilationAssembly(),
                HotReloadTypeHome.ScriptAssembliesUnderProject(ProjectRoot, AssemblyName),
                ProjectRoot,
                sinks);
        }

        // The worker output of a run that found the patched method's body unchanged: no entry to
        // apply, one unchanged row, and no home assembly because the type is served by the
        // assembly the edited file belongs to.
        private static TransformWorkerOutputDto BuildWorkerOutput()
        {
            return new TransformWorkerOutputDto
            {
                entries = new TransformWorkerEntryDto[0],
                skipped = new TransformWorkerSkippedDto[0],
                files = new TransformWorkerFileOutputDto[0],
                unchangedMethods = new[]
                {
                    new TransformWorkerUnchangedMethodDto
                    {
                        sourceProjectRelativePath = OwnerPath,
                        typeMetadataName = FixtureMetadataName,
                        methodName = nameof(HotReloadCoreFixture.StaticPing),
                        parameterTypeFullNames = new string[0],
                        genericArity = 0,
                        homeAssemblyName = null
                    }
                }
            };
        }

        private static string ProjectRoot =>
            Path.GetFullPath(Path.Combine(Application.dataPath, ".."));

        private static UnityCompilationAssembly FindCompilationAssembly()
        {
            foreach (UnityCompilationAssembly assembly
                in UnityEditor.Compilation.CompilationPipeline.GetAssemblies())
            {
                if (assembly.name == AssemblyName)
                {
                    return assembly;
                }
            }

            Assert.Fail("Compilation assembly was not found.");
            return null;
        }
    }
}
