using System;
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
        private const string SameNameFixtureMetadataName =
            "io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.HotReloadPeelSameNameFixture";

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

        /// <summary>
        /// What: with no live patch anywhere, the peel resolves none of the rows, because none of
        /// them can lead to a peel.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_NoLivePatchAnywhere_ResolvesNoRow()
        {
            HotReloadGroupFile file = ArrangeFile();

            PeelRun run = RevertRows(
                file,
                Row(FixtureMetadataName, nameof(HotReloadCoreFixture.StaticPing)),
                Row(SameNameFixtureMetadataName, nameof(HotReloadPeelSameNameFixture.StaticPing)));

            Assert.That(
                run.ResolveCalls,
                Is.EqualTo(0),
                "With nothing patched, resolving a row only reads the compiled assembly for nothing.");
            Assert.That(run.Reverted, Is.EqualTo(0));
            Assert.That(run.Outcomes, Is.Empty);
        }

        /// <summary>
        /// What: a row whose method name no live patch carries is not resolved, and the live patch
        /// on the other method stays.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_LivePatchOnAnotherMethodName_ResolvesNoRow()
        {
            HotReloadGroupFile file = ArrangeUnchangedMethodWithActivePatch();

            PeelRun run = RevertRows(
                file,
                Row(FixtureMetadataName, nameof(HotReloadCoreFixture.VoidBump)));

            Assert.That(
                run.ResolveCalls,
                Is.EqualTo(0),
                "Only StaticPing holds a patch, so a VoidBump row cannot lead to a peel.");
            Assert.That(run.Reverted, Is.EqualTo(0));
            Assert.That(
                HotReloadCoreFixture.StaticPing(),
                Is.EqualTo("patched"),
                "The patch on the method the rows do not name must stay live.");
        }

        /// <summary>
        /// What: the row of the patched method itself is resolved and has its patch peeled.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_RowOfThePatchedMethod_ResolvesAndPeelsIt()
        {
            HotReloadGroupFile file = ArrangeUnchangedMethodWithActivePatch();

            PeelRun run = RevertRows(
                file,
                Row(FixtureMetadataName, nameof(HotReloadCoreFixture.StaticPing)));

            Assert.That(run.ResolveCalls, Is.EqualTo(1));
            Assert.That(run.Reverted, Is.EqualTo(1));
            Assert.That(
                HotReloadCoreFixture.StaticPing(),
                Is.EqualTo("original"),
                "The peeled method must run the code its own assembly holds again.");
        }

        /// <summary>
        /// What: a row of another type whose method shares the patched method's name is still
        /// resolved, and resolving it to that other method keeps the live patch.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_SameNameOnAnotherType_ResolvesButKeepsThePatch()
        {
            HotReloadGroupFile file = ArrangeUnchangedMethodWithActivePatch();

            PeelRun run = RevertRows(
                file,
                Row(SameNameFixtureMetadataName, nameof(HotReloadPeelSameNameFixture.StaticPing)));

            Assert.That(
                run.ResolveCalls,
                Is.EqualTo(1),
                "A shared name only narrows the rows; telling the two methods apart takes resolving.");
            Assert.That(run.Reverted, Is.EqualTo(0));
            Assert.That(
                HotReloadCoreFixture.StaticPing(),
                Is.EqualTo("patched"),
                "A row of another type must not peel the patch of a method that only shares its name.");
        }

        /// <summary>
        /// What: a row missing its type name is skipped before it is resolved.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_RowMissingItsTypeName_IsNotResolved()
        {
            HotReloadGroupFile file = ArrangeUnchangedMethodWithActivePatch();

            PeelRun run = RevertRows(
                file,
                Row(null, nameof(HotReloadCoreFixture.StaticPing)));

            Assert.That(run.ResolveCalls, Is.EqualTo(0));
            Assert.That(run.Reverted, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a row naming an assembly no introduced-type artifact carries stops the peel, even
        /// when no live patch exists for the row to lead to.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_RowNamingAnAssemblyNoArtifactCarries_Throws()
        {
            HotReloadGroupFile file = ArrangeFile();
            TransformWorkerUnchangedMethodDto row =
                Row(FixtureMetadataName, nameof(HotReloadCoreFixture.StaticPing));
            row.homeAssemblyName = "UnchangedPeelUnknownAssembly";

            Assert.Throws<InvalidOperationException>(() => RevertRows(file, row));
        }

        /// <summary>
        /// What: among rows of unpatched methods, only the row named like the live patch is
        /// resolved, and that row has its patch peeled.
        /// </summary>
        [Test]
        public void RevertUnchangedPatches_PatchedRowAmongUnpatchedRows_ResolvesOnlyThePatchedName()
        {
            HotReloadGroupFile file = ArrangeUnchangedMethodWithActivePatch();

            PeelRun run = RevertRows(
                file,
                Row(FixtureMetadataName, nameof(HotReloadCoreFixture.VoidBump)),
                Row(FixtureMetadataName, nameof(HotReloadCoreFixture.StaticPing)),
                Row(FixtureMetadataName, nameof(HotReloadCoreFixture.VoidBump)));

            Assert.That(
                run.ResolveCalls,
                Is.EqualTo(1),
                "The VoidBump rows cannot lead to a peel, so only the StaticPing row is resolved.");
            Assert.That(run.Reverted, Is.EqualTo(1));
        }

        /// <summary>
        /// What: the filter names only the methods that hold a live patch: none before the patch,
        /// StaticPing while it is patched, and none again once the peel has removed that patch.
        /// </summary>
        [Test]
        public void CollectPatchedMethodNames_ListsTheNamesOfLivePatchesOnly()
        {
            HotReloadDomainTestAccess access = new HotReloadDomainTestAccess();
            Assert.That(
                HotReloadUnchangedPeelFilter.CollectPatchedMethodNames(access.Domain.ListGenerations()),
                Is.Empty,
                "Nothing is patched yet.");

            HotReloadGroupFile file = ArrangeUnchangedMethodWithActivePatch();
            Assert.That(
                HotReloadUnchangedPeelFilter.CollectPatchedMethodNames(access.Domain.ListGenerations()),
                Is.EquivalentTo(new[] { nameof(HotReloadCoreFixture.StaticPing) }));

            RevertRows(file, Row(FixtureMetadataName, nameof(HotReloadCoreFixture.StaticPing)));
            Assert.That(
                HotReloadUnchangedPeelFilter.CollectPatchedMethodNames(access.Domain.ListGenerations()),
                Is.Empty,
                "A peeled patch is no longer live, so its name must not keep rows resolving.");
        }

        private static void Revert(HotReloadGroupFile file)
        {
            HotReloadCompositionRoot.Services.EntryApplier.RevertUnchangedPatchesPerFile(
                new[] { file },
                HotReloadWorkerRowsByFile.Build(BuildWorkerOutput(), new[] { OwnerPath }));
        }

        // Runs the peel over the given rows with the production resolver wrapped in a counter.
        private static PeelRun RevertRows(HotReloadGroupFile file, params TransformWorkerUnchangedMethodDto[] rows)
        {
            int resolveCalls = 0;
            HotReloadMethodResolver counting = (home, typeMetadataName, methodName, parameterTypeFullNames, genericArity) =>
            {
                resolveCalls++;
                return HotReloadMethodMatcher.Resolve(
                    home, typeMetadataName, methodName, parameterTypeFullNames, genericArity);
            };
            List<HotReloadMethodOutcome> outcomes = new List<HotReloadMethodOutcome>();
            int reverted = HotReloadCompositionRoot.Services.EntryApplier.RevertUnchangedPatches(
                file.Home,
                new HotReloadEntryHomeResolver(new HotReloadDomainTestAccess().Domain, ProjectRoot),
                rows,
                outcomes,
                file.AssemblyResolvePath,
                counting);
            return new PeelRun(reverted, resolveCalls, outcomes);
        }

        // A worker row for a parameterless, non-generic method of the fixture owner's file.
        private static TransformWorkerUnchangedMethodDto Row(string typeMetadataName, string methodName)
        {
            return new TransformWorkerUnchangedMethodDto
            {
                sourceProjectRelativePath = OwnerPath,
                typeMetadataName = typeMetadataName,
                methodName = methodName,
                parameterTypeFullNames = new string[0],
                genericArity = 0,
                homeAssemblyName = null
            };
        }

        private static HotReloadGroupFile ArrangeUnchangedMethodWithActivePatch()
        {
            HotReloadGroupFile file = ArrangeFile();
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
            return file;
        }

        // The fixture owner's file with no patch installed; a test that needs one adds it.
        private static HotReloadGroupFile ArrangeFile()
        {
            HotReloadFileSinks sinks = new HotReloadFileSinks(new List<string>(), null, new HotReloadRunStaleSignatureWarnings());
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

        // What one peel did: the patches it removed, the rows it resolved, and the outcomes it
        // recorded.
        private sealed class PeelRun
        {
            internal PeelRun(int reverted, int resolveCalls, List<HotReloadMethodOutcome> outcomes)
            {
                Reverted = reverted;
                ResolveCalls = resolveCalls;
                Outcomes = outcomes;
            }

            internal int Reverted { get; }

            internal int ResolveCalls { get; }

            internal List<HotReloadMethodOutcome> Outcomes { get; }
        }
    }
}
