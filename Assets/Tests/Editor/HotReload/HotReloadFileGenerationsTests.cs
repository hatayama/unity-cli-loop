using System;
using System.IO;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the write facade over the two per-file generation registries.
    /// Reads go straight to the registries; only the writes are under test here.
    /// </summary>
    public class HotReloadFileGenerationsTests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/FileGenerationsFixture.cs";

        private Func<MethodBase, MethodBase> _originalActiveShimLookup;

        // Why the stand-in: the file lookup hides methods the patch ledger no longer reports as
        // patched, and these tests register shims without applying a Harmony patch. Reporting
        // every method as patched makes the lookup show the generation's contents as recorded.
        [SetUp]
        public void SetUp()
        {
            _originalActiveShimLookup = HotReloadPausePointCoordination.GetActiveShimForMethod;
            HotReloadPausePointCoordination.GetActiveShimForMethod = method => method;
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPausePointCoordination.GetActiveShimForMethod = _originalActiveShimLookup;
            HotReloadPatcher.RevertAll();
        }

        /// <summary>
        /// What: starting a file generation opens both registries for the path with nothing in them.
        /// </summary>
        [Test]
        public void BeginFileGeneration_StartsBothRegistriesEmpty()
        {
            BeginGeneration();

            Assert.That(HotReloadShimRegistry.HasGeneration(FixtureProjectRelativePath), Is.True);
            Assert.That(HotReloadAddedMemberRegistry.HasGeneration(FixtureProjectRelativePath), Is.True);
            Assert.That(
                HotReloadAddedMemberRegistry.ListActiveMethodKeys(FixtureProjectRelativePath),
                Is.Empty);
        }

        /// <summary>
        /// What: an added-member-only start drops the added members but leaves the shim
        /// generation's registered methods in place.
        /// </summary>
        [Test]
        public void BeginAddedMemberOnlyGeneration_KeepsTheShimGenerationContents()
        {
            BeginGeneration();
            RegisterOneOfEach();

            HotReloadFileGenerations.BeginAddedMemberOnlyGeneration(FixtureProjectRelativePath);

            Assert.That(FindRegisteredShim(), Is.Not.Null);
            Assert.That(
                HotReloadAddedMemberRegistry.ListActiveMethodKeys(FixtureProjectRelativePath),
                Is.Empty);
        }

        /// <summary>
        /// What: clearing drops the generation on both registries, not just one of them.
        /// </summary>
        [Test]
        public void ClearAll_DropsBothRegistries()
        {
            BeginGeneration();
            RegisterOneOfEach();

            HotReloadFileGenerations.ClearAll();

            Assert.That(HotReloadShimRegistry.HasGeneration(FixtureProjectRelativePath), Is.False);
            Assert.That(HotReloadAddedMemberRegistry.HasGeneration(FixtureProjectRelativePath), Is.False);
        }

        /// <summary>
        /// What: removing one shim method takes that method out while the file keeps its
        /// generation, so a sibling registration in the same run still finds the file key.
        /// </summary>
        [Test]
        public void RemoveShimMethod_RemovesTheMethodButKeepsTheGeneration()
        {
            BeginGeneration();
            RegisterOneOfEach();

            HotReloadFileGenerations.RemoveShimMethod(GetShimTarget());

            Assert.That(FindRegisteredShim(), Is.Null);
            Assert.That(HotReloadShimRegistry.HasGeneration(FixtureProjectRelativePath), Is.True);
        }

        private static void BeginGeneration()
        {
            Assembly assembly = typeof(HotReloadFileGenerationsTests).Assembly;
            byte[] assemblyBytes = File.ReadAllBytes(assembly.Location);
            HotReloadFileGenerations.BeginFileGeneration(
                FixtureProjectRelativePath,
                assemblyBytes,
                null,
                assembly);
        }

        private static void RegisterOneOfEach()
        {
            HotReloadFileGenerations.RegisterShimMethod(
                FixtureProjectRelativePath,
                GetShimTarget(),
                new HotReloadShimRegistry.MethodEntry(GetAddedTarget(), false, 1, 2));
            HotReloadFileGenerations.RegisterAddedMethod(
                FixtureProjectRelativePath,
                "FileGenerationsFixture.AddedMember()",
                GetAddedTarget(),
                FixtureProjectRelativePath);
        }

        private static HotReloadShimMethodLookup FindRegisteredShim()
        {
            HotReloadShimFileLookup lookup =
                HotReloadPausePointCoordination.GetShimLookupForFile?.Invoke(FixtureProjectRelativePath);
            if (lookup == null)
            {
                return null;
            }

            return lookup.Methods.FirstOrDefault(method => method.OriginalMethod == GetShimTarget());
        }

        private static MethodInfo GetShimTarget()
        {
            return typeof(HotReloadFileGenerationsTests).GetMethod(
                nameof(ShimTarget),
                BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static MethodInfo GetAddedTarget()
        {
            return typeof(HotReloadFileGenerationsTests).GetMethod(
                nameof(AddedTarget),
                BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static int ShimTarget()
        {
            return 1;
        }

        private static int AddedTarget()
        {
            return 2;
        }
    }
}
