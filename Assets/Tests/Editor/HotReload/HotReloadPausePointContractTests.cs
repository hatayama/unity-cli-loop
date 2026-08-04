using System;
using System.Reflection;
using System.Runtime.CompilerServices;

using HarmonyLib;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// EditMode coverage for the hot-reload / source pause-point contract:
    /// reject new markers on patched methods, and restore arming after RevertAll.
    /// </summary>
    public class HotReloadPausePointContractTests
    {
        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPatcher.RevertAll();
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// What: Patching a pause point onto a method that is already hot-reload patched
        /// returns MethodPatchedByHotReload instead of crashing or silently injecting.
        /// </summary>
        [Test]
        public void Patch_OnHotReloadedMethod_ReturnsMethodPatchedByHotReload()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointContractFixture),
                nameof(HotReloadPausePointContractFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.ReplaceableCompute__shim0));

            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);

            SourcePausePointResolution resolution = BuildSyntheticResolution(original, instructionIndex: 5000);
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "contract-reject-on-patched",
                resolution);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.FailureReason,
                Is.EqualTo(SourcePausePointPatchFailureReason.MethodPatchedByHotReload));
        }

        /// <summary>
        /// What: After RevertAll, Patching a pause point onto the previously patched method
        /// succeeds, proving the rejection is not permanent.
        /// </summary>
        [Test]
        public void Patch_AfterRevertAll_Succeeds()
        {
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointContractFixture),
                nameof(HotReloadPausePointContractFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.ReplaceableCompute__shim0));

            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);
            HotReloadPatcher.RevertAll();

            SourcePausePointResolution resolution = BuildSyntheticResolution(original, instructionIndex: 0);
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "contract-ok-after-revert",
                resolution);

            Assert.That(result.Success, Is.True, result.ErrorMessage);
        }

        /// <summary>
        /// What: Applying a hot-reload patch to a method with an armed marker sets
        /// SuppressedByHotReload, and RevertAll clears the flag.
        /// </summary>
        [Test]
        public void Apply_MethodWithArmedMarker_SetsAndClearsSuppressedFlag()
        {
            const string id = "contract-suppress-flag";
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointContractFixture),
                nameof(HotReloadPausePointContractFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.ReplaceableCompute__shim0));

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(
                SourcePausePointPatcher.Patch(id, BuildSyntheticResolution(original, instructionIndex: 0)).Success,
                Is.True);

            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);
            Assert.That(UloopPausePointRegistry.GetStatus(id).SuppressedByHotReload, Is.True);

            HotReloadPatcher.RevertAll();
            Assert.That(UloopPausePointRegistry.GetStatus(id).SuppressedByHotReload, Is.False);
        }

        /// <summary>
        /// What: An armed deep marker does not make a subsequent hot-reload Apply fail, and
        /// the marker is reported as SuppressedByHotReload after Apply succeeds.
        /// </summary>
        [Test]
        public void Apply_MethodWithDeepArmedMarker_SucceedsAndSuppresses()
        {
            const string id = "contract-deep-suppress";
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointDeepFixture),
                nameof(HotReloadPausePointDeepFixture.DeepStatements));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.DeepStatements__shim0));

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(
                SourcePausePointPatcher.Patch(id, BuildSyntheticResolution(original, instructionIndex: 10)).Success,
                Is.True);

            HotReloadPatchResult applyResult = HotReloadPatcher.Apply(
                original, shim, HotReloadPatchShape.Delegation);
            Assert.That(applyResult.Success, Is.True, applyResult.ErrorMessage);
            Assert.That(UloopPausePointRegistry.GetStatus(id).SuppressedByHotReload, Is.True);
        }

        /// <summary>
        /// What: Unpatching one of two armed markers while the method is hot-reload patched
        /// does not re-instrument the surviving marker into the shim stream (and does not throw).
        /// </summary>
        [Test]
        public void Unpatch_SiblingMarker_WhileHotReloadPatched_DoesNotReinstrumentSurvivors()
        {
            const string id1 = "contract-sibling-unpatch-a";
            const string id2 = "contract-sibling-unpatch-b";
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointDeepFixture),
                nameof(HotReloadPausePointDeepFixture.DeepStatements));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.DeepStatements__shim0));

            UloopPausePointRegistry.Enable(id1, 30);
            Assert.That(
                SourcePausePointPatcher.Patch(id1, BuildSyntheticResolution(original, instructionIndex: 0)).Success,
                Is.True);
            UloopPausePointRegistry.Enable(id2, 30);
            Assert.That(
                SourcePausePointPatcher.Patch(id2, BuildSyntheticResolution(original, instructionIndex: 10)).Success,
                Is.True);

            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Delegation).Success,
                Is.True);

            Assert.DoesNotThrow(() => SourcePausePointPatcher.Unpatch(id1));

            HotReloadPausePointDeepFixture fixture = new HotReloadPausePointDeepFixture();
            Assert.That(fixture.DeepStatements(), Is.EqualTo(99));
            Assert.That(UloopPausePointRegistry.GetStatus(id2).IsHit, Is.False);
        }

        /// <summary>
        /// What: RevertAll restores armed-marker instrumentation so invoking the fixture
        /// records a hit again (regression for ledger-clear-before-UnpatchAll ordering).
        /// </summary>
        [Test]
        public void RevertAll_RestoresArmedMarkerInstrumentation()
        {
            const string id = "contract-restore-hit";
            MethodInfo original = AccessTools.Method(
                typeof(HotReloadPausePointContractFixture),
                nameof(HotReloadPausePointContractFixture.ReplaceableCompute));
            MethodInfo shim = AccessTools.Method(
                typeof(HotReloadPausePointContractShims),
                nameof(HotReloadPausePointContractShims.ReplaceableCompute__shim0));

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(
                SourcePausePointPatcher.Patch(id, BuildSyntheticResolution(original, instructionIndex: 0)).Success,
                Is.True);

            Assert.That(
                HotReloadPatcher.Apply(original, shim, HotReloadPatchShape.Transplant).Success,
                Is.True);
            HotReloadPatcher.RevertAll();

            HotReloadPausePointContractFixture fixture = new HotReloadPausePointContractFixture();
            int result = fixture.ReplaceableCompute(5);
            Assert.That(result, Is.EqualTo(-5));

            UloopPausePointSnapshot status = UloopPausePointRegistry.GetStatus(id);
            Assert.That(status.IsHit, Is.True);
            Assert.That(status.HitCount, Is.EqualTo(1));
        }

        private static SourcePausePointResolution BuildSyntheticResolution(
            MethodBase method,
            int instructionIndex)
        {
            return new SourcePausePointResolution(
                method.Module.Assembly.GetName().Name,
                method.Module.ModuleVersionId.ToString(),
                method.MetadataToken,
                method.ToString(),
                method.IsStatic,
                method.DeclaringType.IsValueType,
                instructionIndex,
                0,
                1,
                Array.Empty<SourcePausePointLocalVariable>(),
                Array.Empty<SourcePausePointParameter>());
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public int PauseCount { get; private set; }
            public bool IsPlaying => true;
            public bool IsPaused => PauseCount > 0;

            public void Pause()
            {
                PauseCount++;
            }

            public void Resume()
            {
                PauseCount = 0;
            }
        }
    }

    /// <summary>
    /// NoInlining fixture used only by the hot-reload / pause-point contract tests.
    /// </summary>
    public class HotReloadPausePointContractFixture
    {
        // Why NoInlining: patch-target fixtures must not be inlined into the test method
        // that was JIT-compiled before the patch was applied.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReplaceableCompute(int delta)
        {
            return -1 * delta;
        }
    }

    /// <summary>
    /// NoInlining fixture with enough independent statements for a deep InstructionIndex arm.
    /// </summary>
    public class HotReloadPausePointDeepFixture
    {
        // Why NoInlining: patch-target fixtures must not be inlined into the test method
        // that was JIT-compiled before the patch was applied.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DeepStatements()
        {
            int a = 1;
            int b = 2;
            int c = 3;
            int d = 4;
            int e = 5;
            int f = 6;
            int g = 7;
            int h = 8;
            int i = 9;
            int j = 10;
            int k = 11;
            int l = 12;
            return a + b + c + d + e + f + g + h + i + j + k + l;
        }
    }

    /// <summary>
    /// Hand-written transplant / delegation shims for the contract fixtures.
    /// </summary>
    public static class HotReloadPausePointContractShims
    {
        public static int ReplaceableCompute__shim0(HotReloadPausePointContractFixture instance, int delta)
        {
            return delta + 42;
        }

        public static int DeepStatements__shim0(HotReloadPausePointDeepFixture instance)
        {
            return 99;
        }
    }
}
