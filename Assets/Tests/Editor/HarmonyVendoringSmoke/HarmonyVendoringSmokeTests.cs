using HarmonyLib;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Proves the vendored, renamed Harmony DLL (UnityCliLoop.0Harmony.dll) actually loads and
    /// patches methods inside the Unity Editor Mono runtime at test time, not just compiles.
    /// Compile success alone would not catch a rename side effect or a MonoMod detour backend
    /// failure on this runtime, both of which only surface when Harmony actually runs.
    /// </summary>
    public class HarmonyVendoringSmokeTests
    {
        private const string HarmonyId = "io.github.hatayama.uloop.harmony-vendoring-smoke-test";

        private static class PatchTarget
        {
            public static bool WasCalled;

            public static void Method()
            {
                WasCalled = true;
            }
        }

        private static class PatchTargetPrefix
        {
            public static bool PrefixRan;

            public static bool Prefix()
            {
                PrefixRan = true;
                return true;
            }
        }

        [TearDown]
        public void TearDown()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        }

        /// <summary>What: a Harmony prefix patch actually executes when the patched method runs.</summary>
        [Test]
        public void PatchedMethod_WhenInvoked_RunsPrefix()
        {
            PatchTarget.WasCalled = false;
            PatchTargetPrefix.PrefixRan = false;
            Harmony harmony = new(HarmonyId);
            harmony.Patch(
                typeof(PatchTarget).GetMethod(nameof(PatchTarget.Method)),
                prefix: new HarmonyMethod(typeof(PatchTargetPrefix).GetMethod(nameof(PatchTargetPrefix.Prefix))));

            PatchTarget.Method();

            Assert.IsTrue(PatchTargetPrefix.PrefixRan);
            Assert.IsTrue(PatchTarget.WasCalled);
        }

        /// <summary>What: UnpatchAll removes the prefix and restores the method's original behavior.</summary>
        [Test]
        public void UnpatchAll_AfterPatch_RestoresOriginalBehavior()
        {
            Harmony harmony = new(HarmonyId);
            harmony.Patch(
                typeof(PatchTarget).GetMethod(nameof(PatchTarget.Method)),
                prefix: new HarmonyMethod(typeof(PatchTargetPrefix).GetMethod(nameof(PatchTargetPrefix.Prefix))));

            harmony.UnpatchAll(HarmonyId);
            PatchTarget.WasCalled = false;
            PatchTargetPrefix.PrefixRan = false;
            PatchTarget.Method();

            Assert.IsFalse(PatchTargetPrefix.PrefixRan);
            Assert.IsTrue(PatchTarget.WasCalled);
        }
    }
}
