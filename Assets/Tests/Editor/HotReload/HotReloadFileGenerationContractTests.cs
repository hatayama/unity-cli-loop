using System;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The invariants one file's hot-reload generation refuses to break. Each condition gets its
    /// own test, so a rejection that covers two of them cannot hide a missing check.
    /// </summary>
    public class HotReloadFileGenerationContractTests
    {
        private const string FixtureProjectRelativePath =
            "Assets/Tests/Editor/HotReload/FileGenerationContractFixture.cs";
        private const string AddedMethodKey = "FileGenerationContractFixture.AddedMember()";

        // Any non-empty byte array satisfies a shim generation no test loads bytes from.
        private static readonly byte[] PlaceholderAssemblyBytes = { 0x4D, 0x5A };

        /// <summary>
        /// What: a patch of a method whose shim this generation never registered is refused, which
        /// is the invariant that every patch is a patch of a method the edited source declares.
        /// </summary>
        [Test]
        public void BeginPatch_WithoutARegisteredShim_Throws()
        {
            HotReloadFileGeneration generation = CreateStartedGeneration();

            Assert.Throws<InvalidOperationException>(
                () => generation.BeginPatch(GetShimTarget(), GetAddedTarget()));
            Assert.That(generation.HasPatch(GetShimTarget()), Is.False);
        }

        /// <summary>
        /// What: opening a second patch of a method that already holds a pending one is refused, so
        /// a pending entry a Harmony call is still reading cannot be replaced under it.
        /// </summary>
        [Test]
        public void BeginPatch_WhenThePatchIsPending_Throws()
        {
            HotReloadFileGeneration generation = CreateStartedGeneration();
            RegisterShim(generation);
            generation.BeginPatch(GetShimTarget(), GetAddedTarget());

            Assert.Throws<InvalidOperationException>(
                () => generation.BeginPatch(GetShimTarget(), GetAddedTarget()));
            Assert.That(generation.IsPatchPending(GetShimTarget()), Is.True);
        }

        /// <summary>
        /// What: opening a patch of a method that already holds a live one is refused, so a
        /// re-apply that skipped the deactivation cannot lose the live entry.
        /// </summary>
        [Test]
        public void BeginPatch_WhenThePatchIsActive_Throws()
        {
            HotReloadFileGeneration generation = CreateStartedGeneration();
            RegisterShim(generation);
            generation.BeginPatch(GetShimTarget(), GetAddedTarget());
            generation.CommitPatch(GetShimTarget());

            Assert.Throws<InvalidOperationException>(
                () => generation.BeginPatch(GetShimTarget(), GetAddedTarget()));
            Assert.That(generation.IsPatchActive(GetShimTarget()), Is.True);
        }

        /// <summary>
        /// What: committing a patch that was never opened is refused, so a run cannot report a
        /// live patch Harmony was never asked to install.
        /// </summary>
        [Test]
        public void CommitPatch_WithoutAPendingPatch_Throws()
        {
            HotReloadFileGeneration generation = CreateStartedGeneration();
            RegisterShim(generation);

            Assert.Throws<InvalidOperationException>(() => generation.CommitPatch(GetShimTarget()));
            Assert.That(generation.IsPatchActive(GetShimTarget()), Is.False);
        }

        /// <summary>
        /// What: registering a shim before the shim generation is opened is refused, so a
        /// registration cannot land in a generation the next apply run will not clear.
        /// </summary>
        [Test]
        public void RegisterShimMethod_BeforeTheGenerationIsOpened_Throws()
        {
            HotReloadFileGeneration generation = new HotReloadFileGeneration(FixtureProjectRelativePath);

            Assert.Throws<InvalidOperationException>(() => RegisterShim(generation));
            Assert.That(generation.FindShim(GetShimTarget()), Is.Null);
        }

        /// <summary>
        /// What: registering an added member before the added-member generation is opened is
        /// refused, for the same reason the shim registration is.
        /// </summary>
        [Test]
        public void RegisterAddedMethod_BeforeTheGenerationIsOpened_Throws()
        {
            HotReloadFileGeneration generation = new HotReloadFileGeneration(FixtureProjectRelativePath);

            Assert.Throws<InvalidOperationException>(
                () => generation.RegisterAddedMethod(
                    AddedMethodKey,
                    GetAddedTarget(),
                    FixtureProjectRelativePath));
            Assert.That(generation.AddedMemberCount, Is.EqualTo(0));
        }

        /// <summary>
        /// What: dropping the shim of a method whose patch is still live is refused, because that
        /// is the operation that would leave a live patch of a method with no registered shim.
        /// </summary>
        [Test]
        public void RemoveShimMethod_WhileThePatchIsActive_Throws()
        {
            HotReloadFileGeneration generation = CreateStartedGeneration();
            RegisterShim(generation);
            generation.BeginPatch(GetShimTarget(), GetAddedTarget());
            generation.CommitPatch(GetShimTarget());

            Assert.Throws<InvalidOperationException>(
                () => generation.RemoveShimMethod(GetShimTarget()));
            Assert.That(generation.FindShim(GetShimTarget()), Is.Not.Null);
        }

        private static HotReloadFileGeneration CreateStartedGeneration()
        {
            HotReloadFileGeneration generation =
                new HotReloadFileGeneration(FixtureProjectRelativePath);
            generation.BeginShimGeneration(
                PlaceholderAssemblyBytes,
                null,
                typeof(HotReloadFileGenerationContractTests).Assembly);
            return generation;
        }

        private static void RegisterShim(HotReloadFileGeneration generation)
        {
            generation.RegisterShimMethod(
                GetShimTarget(),
                new HotReloadShimMethodEntry(GetAddedTarget(), false, 1, 2));
        }

        private static MethodInfo GetShimTarget()
        {
            return typeof(HotReloadFileGenerationContractTests).GetMethod(
                nameof(ShimTarget),
                BindingFlags.Static | BindingFlags.NonPublic);
        }

        private static MethodInfo GetAddedTarget()
        {
            return typeof(HotReloadFileGenerationContractTests).GetMethod(
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
