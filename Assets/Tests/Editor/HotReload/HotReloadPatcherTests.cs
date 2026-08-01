using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using HarmonyLib;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for <see cref="HotReloadPatcher"/> transplant apply / revert / re-apply.
    /// </summary>
    public class HotReloadPatcherTests
    {
        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
        }

        /// <summary>
        /// What: transplanting a handwritten instance shim changes the method's return value.
        /// </summary>
        [Test]
        public void Apply_InstanceReturnValueShim_ChangesBehavior()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(-5), "Precondition: original sentinel body.");

            HotReloadPatchResult result = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Transplant);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(47));
        }

        /// <summary>
        /// What: a static shim transplant changes StaticPing's return value.
        /// </summary>
        [Test]
        public void Apply_StaticShim_ChangesBehavior()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.StaticPing));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.StaticPing__shim0));

            Assert.That(HotReloadCoreFixture.StaticPing(), Is.EqualTo("original"));
            HotReloadPatchResult result = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Transplant);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(HotReloadCoreFixture.StaticPing(), Is.EqualTo("patched"));
        }

        /// <summary>
        /// What: a void instance shim transplant runs and mutates instance state.
        /// </summary>
        [Test]
        public void Apply_VoidInstanceShim_ChangesBehavior()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.VoidBump));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.VoidBump__shim0));

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            fixture.VoidBump();
            Assert.That(fixture.VoidHits, Is.EqualTo(-1), "Precondition: original void body.");

            HotReloadPatchResult result = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Transplant);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            fixture.VoidBump();
            Assert.That(fixture.VoidHits, Is.EqualTo(7));
        }

        /// <summary>
        /// What: RevertAll restores the original method body and clears the ledger.
        /// </summary>
        [Test]
        public void RevertAll_RestoresOriginalBehavior()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(47));

            HotReloadPatcher.RevertAll();
            Assert.That(HotReloadPatcher.ActivePatchCount, Is.EqualTo(0));
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(-5));
        }

        /// <summary>
        /// What: applying a second transplant to the same method leaves exactly one owned transpiler.
        /// </summary>
        [Test]
        public void Apply_Twice_LeavesSingleTranspiler()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim0 = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
            MethodInfo shim1 = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim1));

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            Assert.That(
                HotReloadPatcher.Apply(original, shim0, HotReloadPatchShape.Transplant).Success,
                Is.True);
            Assert.That(fixture.ReplaceableCompute(1), Is.EqualTo(43));

            Assert.That(
                HotReloadPatcher.Apply(original, shim1, HotReloadPatchShape.Transplant).Success,
                Is.True);
            Assert.That(fixture.ReplaceableCompute(1), Is.EqualTo(100));

            Patches patchInfo = Harmony.GetPatchInfo(original);
            Assert.That(patchInfo, Is.Not.Null);
            Patch[] ownedTranspilers = patchInfo.Transpilers
                .Where(patch => patch.owner == HotReloadConstants.HarmonyId)
                .ToArray();
            Assert.That(ownedTranspilers.Length, Is.EqualTo(1), "Re-apply must not stack hot-reload transpilers.");
            Assert.That(HotReloadPatcher.ActivePatchCount, Is.EqualTo(1));
        }

        /// <summary>
        /// What: value-type methods are rejected before Harmony is invoked.
        /// </summary>
        [Test]
        public void Apply_ValueTypeMethod_IsRejected()
        {
            MethodInfo original = AccessTools.Method(typeof(HotReloadValueTypeFixture), nameof(HotReloadValueTypeFixture.Compute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));

            HotReloadPatchResult result = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Transplant);
            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadPatchFailureReason.UnpatchableValueType));
        }

        /// <summary>
        /// What: a JIT-legal async shim applied via Delegation changes the await result, and
        /// RevertAll restores the original sentinel body.
        /// </summary>
        [Test]
        public async Task Apply_DelegationAsyncShim_ChangesBehaviorAndReverts()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableComputeAsync));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims),
                nameof(HotReloadHandwrittenShims.ReplaceableComputeAsync__shim0));

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            Assert.That(
                await fixture.ReplaceableComputeAsync(5),
                Is.EqualTo(-5),
                "Precondition: original async sentinel body.");

            HotReloadPatchResult result = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Delegation);
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            Assert.That(await fixture.ReplaceableComputeAsync(5), Is.EqualTo(10 + 5 + 1));

            HotReloadPatcher.RevertAll();
            Assert.That(HotReloadPatcher.ActivePatchCount, Is.EqualTo(0));
            Assert.That(await fixture.ReplaceableComputeAsync(5), Is.EqualTo(-5));
        }
    }

    /// <summary>
    /// Struct fixture used only to assert the v1 value-type rejection path.
    /// </summary>
    public struct HotReloadValueTypeFixture
    {
        public int Compute(int delta)
        {
            return delta;
        }
    }
}
