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
    /// EditMode coverage for the peel of the patches a retained introduced-type declaration
    /// carries, which the commit stage runs beside the peel of superseded method patches.
    /// </summary>
    public class HotReloadRetainedTypePatchReverterTests
    {
        private const string AssemblyName = "UnityCLILoop.Tests.Editor.HotReload";
        private const string OwnerPath = "Assets/Tests/Editor/HotReload/HotReloadCoreFixtures.cs";
        private const string TargetAssemblyMvid = "retained-type-peel-mvid";
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
        /// installed on its retained declaration instead of having it peeled as stale.
        /// </summary>
        [Test]
        public void RevertRestoredBodies_FileTheShimCompileRefusedToApply_KeepsThePreviousRunsPatch()
        {
            HotReloadGroupFile file = ArrangeRetainedDeclarationWithActivePatch();
            file.SkipApply = true;

            Revert(file);

            Assert.That(
                file.RevertedUnchangedCount,
                Is.EqualTo(0),
                "A file this run does not apply must keep the previous run's patches, so the peel "
                + "of its retained declaration has to be skipped along with the apply.");
            Assert.That(
                HotReloadCoreFixture.StaticPing(),
                Is.EqualTo("patched"),
                "The patch of the previous reload must still be live.");
        }

        /// <summary>
        /// What: a file this run does apply has the patch of the previous reload peeled off its
        /// retained declaration, so the artifact runs its own code again.
        /// </summary>
        [Test]
        public void RevertRestoredBodies_FileThisRunApplies_PeelsThePreviousRunsPatch()
        {
            HotReloadGroupFile file = ArrangeRetainedDeclarationWithActivePatch();

            Revert(file);

            Assert.That(
                file.RevertedUnchangedCount,
                Is.EqualTo(1),
                "A retained declaration this run bound without a body change has nothing left to "
                + "patch, so the patch of the previous reload is peeled.");
            Assert.That(
                HotReloadCoreFixture.StaticPing(),
                Is.EqualTo("original"),
                "The peeled method must run the code its own assembly holds again.");
        }

        private static void Revert(HotReloadGroupFile file)
        {
            new HotReloadRetainedTypePatchReverter(
                    HotReloadCompositionRoot.Services.Patcher,
                    HotReloadCompositionRoot.Services.Domain.IntroducedTypes)
                .RevertRestoredBodiesPerFile(new[] { file }, AssemblyName, TargetAssemblyMvid);
        }

        // Why a fixture type and a fixture assembly stand in for an artifact: the peel identifies
        // a declaration by the assembly that declares it and the artifacts the registry holds for
        // the run's target, neither of which requires the assembly to have been emitted by a
        // preparation. Compiling one here would only make the test slower to fail.
        private static HotReloadGroupFile ArrangeRetainedDeclarationWithActivePatch()
        {
            HotReloadIntroducedTypeArtifact artifact = new HotReloadIntroducedTypeArtifact(
                typeof(HotReloadCoreFixture).Assembly,
                "Library/UloopHotReload/IntroducedTypes/retained-type-peel.dll",
                null,
                new[]
                {
                    new HotReloadIntroducedTypeDescriptor(
                        AssemblyName,
                        TargetAssemblyMvid,
                        FixtureMetadataName,
                        OwnerPath,
                        "retained-type-peel-fingerprint",
                        null)
                });
            // The order the preparation and the commit boundary use: only a prepared artifact can
            // be activated, so a test that activated directly would be refused.
            HotReloadIntroducedTypeRegistry registry =
                HotReloadCompositionRoot.Services.Domain.IntroducedTypes;
            registry.RegisterPrepared(artifact);
            registry.Activate(artifact);

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

            return CreateFileBoundToTheRetainedDeclaration();
        }

        private static HotReloadGroupFile CreateFileBoundToTheRetainedDeclaration()
        {
            HotReloadFileSinks sinks = new HotReloadFileSinks(new List<string>(), null);
            // The row the preparation leaves for a declaration a retained artifact serves and
            // whose method bodies the edited source matches again.
            sinks.IntroducedTypes.Add(
                HotReloadIntroducedTypeOutcome.AlreadyActive(
                    FixtureMetadataName,
                    AssemblyName,
                    OwnerPath,
                    bodyEdited: false));
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
