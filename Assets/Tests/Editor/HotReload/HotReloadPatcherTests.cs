using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;

using HarmonyLib;
using NUnit.Framework;

using Newtonsoft.Json.Linq;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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
        /// What: DescribeActivePatches lists the patched fixture method after Apply and is empty
        /// after RevertAll.
        /// </summary>
        [Test]
        public void DescribeActivePatches_AfterApply_ListsFixture_AndClearsAfterRevertAll()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));

            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);

            IReadOnlyList<string> afterApply = HotReloadPatcher.DescribeActivePatches();
            Assert.That(afterApply.Count, Is.EqualTo(1));
            Assert.That(
                afterApply[0],
                Does.Contain(nameof(HotReloadCoreFixture)).And.Contain(nameof(HotReloadCoreFixture.ReplaceableCompute)));

            HotReloadPatcher.RevertAll();
            IReadOnlyList<string> afterRevert = HotReloadPatcher.DescribeActivePatches();
            Assert.That(afterRevert, Is.Empty);
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

        [DllImport("__Internal")]
        private static extern int ExternShimStub(int value);

        /// <summary>
        /// What: an apply-time engine failure (here: a body-less extern shim whose
        /// transplant JIT-compiles to invalid IL) returns a per-method Failure instead
        /// of throwing, and leaves no ledger entry or active patch behind.
        /// </summary>
        [Test]
        public void Apply_UnreadableShim_ReturnsFailureAndKeepsLedgerClean()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPatcherTests), nameof(ExternShimStub));

            HotReloadPatchResult result = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Transplant);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(HotReloadPatchFailureReason.ApplyFailed));
            Assert.That(result.ErrorMessage, Does.Contain("ReplaceableCompute"));
            Assert.That(HotReloadPatcher.ActivePatchCount, Is.EqualTo(0));

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(-5), "Original body must survive.");
        }

        /// <summary>
        /// What: each call into a transplanted body increments the invocation registry, --status
        /// reports that count, and RevertAll clears it (removing the IL Increment would leave 0).
        /// </summary>
        [Test]
        public async Task Apply_ThenInvoke_IncrementsStatusCount_AndRevertAllClears()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
            string methodKey = HotReloadPatcher.FormatMethodKey(original);

            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(0L));
            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);
            Assert.That(
                HotReloadInvocationRegistry.GetCount(methodKey),
                Is.EqualTo(0L),
                "Count must stay 0 until the patched body actually runs.");

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(47));
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(47));
            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(2L));

            HotReloadTool tool = new HotReloadTool();
            UnityCliLoopToolResponse baseResponse = await tool.ExecuteAsync(
                new JObject { ["Status"] = true },
                CancellationToken.None);
            HotReloadResponse status = baseResponse as HotReloadResponse;
            Assert.That(status, Is.Not.Null);
            Assert.That(status.Success, Is.True);
            Assert.That(status.Methods.Count, Is.EqualTo(1));
            Assert.That(status.Methods[0].Method, Is.EqualTo(methodKey));
            Assert.That(status.Methods[0].InvocationCount, Is.EqualTo(2L));

            HotReloadPatcher.RevertAll();
            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(0L));
            Assert.That(HotReloadPatcher.DescribeActivePatches(), Is.Empty);
        }

        /// <summary>
        /// What: re-applying a patch removes the previous invocation count so status does not
        /// keep pre-reapply totals.
        /// </summary>
        [Test]
        public void Apply_Twice_ResetsInvocationCount()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim0 = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
            MethodInfo shim1 = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim1));
            string methodKey = HotReloadPatcher.FormatMethodKey(original);

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            Assert.That(
                HotReloadPatcher.Apply(original, shim0, HotReloadPatchShape.Transplant).Success,
                Is.True);
            Assert.That(fixture.ReplaceableCompute(1), Is.EqualTo(43));
            Assert.That(fixture.ReplaceableCompute(1), Is.EqualTo(43));
            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(2L));

            Assert.That(
                HotReloadPatcher.Apply(original, shim1, HotReloadPatchShape.Transplant).Success,
                Is.True);
            Assert.That(
                HotReloadInvocationRegistry.GetCount(methodKey),
                Is.EqualTo(0L),
                "Re-apply must drop the prior generation's count.");
            Assert.That(fixture.ReplaceableCompute(1), Is.EqualTo(100));
            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(1L));
        }

        /// <summary>
        /// What: FormatMethodKey includes parameter type strings so overloads do not share a
        /// counter key (name-only keys would merge counts across Compute(int) and Compute(string)).
        /// </summary>
        [Test]
        public void FormatMethodKey_DistinguishesOverloadsByParameterTypes()
        {
            MethodInfo intOverload = AccessTools.Method(
                typeof(HotReloadOverloadKeyFixture),
                nameof(HotReloadOverloadKeyFixture.Compute),
                new[] { typeof(int) });
            MethodInfo stringOverload = AccessTools.Method(
                typeof(HotReloadOverloadKeyFixture),
                nameof(HotReloadOverloadKeyFixture.Compute),
                new[] { typeof(string) });

            string intKey = HotReloadPatcher.FormatMethodKey(intOverload);
            string stringKey = HotReloadPatcher.FormatMethodKey(stringOverload);

            Assert.That(intKey, Is.Not.EqualTo(stringKey));
            Assert.That(intKey, Does.Contain("System.Int32"));
            Assert.That(stringKey, Does.Contain("System.String"));
        }

        /// <summary>
        /// What: constructed generic parameters use Type.ToString (List`1[System.Int32]), not
        /// assembly-qualified FullName (FullName would embed Version/PublicKeyToken).
        /// </summary>
        [Test]
        public void FormatMethodKey_ConstructedGenericParameter_OmitsAssemblyQualification()
        {
            MethodInfo take = AccessTools.Method(
                typeof(HotReloadGenericKeyFixture),
                nameof(HotReloadGenericKeyFixture.Take));
            string key = HotReloadPatcher.FormatMethodKey(take);

            Assert.That(key, Does.Contain("System.Collections.Generic.List`1[System.Int32]"));
            Assert.That(key, Does.Contain("System.Collections.Generic.Dictionary`2[System.String,System.Int32]"));
            Assert.That(key, Does.Not.Contain("Version="));
            Assert.That(key, Does.Not.Contain("PublicKeyToken="));
            Assert.That(key, Does.Not.Contain("mscorlib"));
            Assert.That(key, Does.Not.Contain("[["));
        }

        /// <summary>
        /// What: worker-shaped FormatMethodKeyParts matches FormatMethodKey(MethodBase) so apply
        /// Methods[].Method and --status Active rows use the same label (including '()' and
        /// Cecil '/' → reflection '+' nested separators).
        /// </summary>
        [Test]
        public void FormatMethodKeyParts_MatchesFormatMethodKey_IncludingNestedCecilSeparators()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            string fromMethod = HotReloadPatcher.FormatMethodKey(original);
            string fromParts = HotReloadPatcher.FormatMethodKeyParts(
                typeof(HotReloadCoreFixture).FullName,
                nameof(HotReloadCoreFixture.ReplaceableCompute),
                new[] { typeof(int).ToString() },
                genericArity: 0);

            Assert.That(fromParts, Is.EqualTo(fromMethod));
            Assert.That(fromParts, Does.EndWith("(System.Int32)"));

            MethodInfo nested = AccessTools.Method(
                typeof(HotReloadNestedKeyFixture.Inner),
                nameof(HotReloadNestedKeyFixture.Inner.Ping));
            string nestedFromMethod = HotReloadPatcher.FormatMethodKey(nested);
            string cecilStyleTypeName = typeof(HotReloadNestedKeyFixture.Inner).FullName.Replace('+', '/');
            string nestedFromParts = HotReloadPatcher.FormatMethodKeyParts(
                cecilStyleTypeName,
                nameof(HotReloadNestedKeyFixture.Inner.Ping),
                System.Array.Empty<string>(),
                genericArity: 0);

            Assert.That(nestedFromParts, Is.EqualTo(nestedFromMethod));
            Assert.That(nestedFromParts, Does.Contain("+Inner.Ping()"));
            Assert.That(nestedFromParts, Does.Not.Contain("/Inner"));
        }

        /// <summary>
        /// What: Revert(method) clears that method's invocation count (RevertAll is not required).
        /// </summary>
        [Test]
        public void Revert_ClearsInvocationCountForThatMethod()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadCoreFixture), nameof(HotReloadCoreFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadHandwrittenShims), nameof(HotReloadHandwrittenShims.ReplaceableCompute__shim0));
            string methodKey = HotReloadPatcher.FormatMethodKey(original);

            HotReloadCoreFixture fixture = new HotReloadCoreFixture();
            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(47));
            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(1L));

            Assert.That(HotReloadPatcher.Revert(original), Is.True);
            Assert.That(HotReloadInvocationRegistry.GetCount(methodKey), Is.EqualTo(0L));
            Assert.That(fixture.ReplaceableCompute(5), Is.EqualTo(-5));
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

    /// <summary>
    /// Overload pair used only to assert FormatMethodKey distinguishes parameter types.
    /// </summary>
    public sealed class HotReloadOverloadKeyFixture
    {
        public int Compute(int delta)
        {
            return delta;
        }

        public int Compute(string label)
        {
            return label == null ? 0 : label.Length;
        }
    }

    /// <summary>
    /// Nested type used only to assert Cecil '/' labels normalize to reflection '+'.
    /// </summary>
    public sealed class HotReloadNestedKeyFixture
    {
        public sealed class Inner
        {
            public int Ping()
            {
                return 1;
            }
        }
    }

    /// <summary>
    /// Constructed-generic parameters used only to assert FormatMethodKey omits assembly quals.
    /// </summary>
    public sealed class HotReloadGenericKeyFixture
    {
        public void Take(
            System.Collections.Generic.List<int> values,
            System.Collections.Generic.Dictionary<string, int> byName)
        {
        }
    }
}
