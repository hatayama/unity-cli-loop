using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Spike S3 for hot reload: proves that hand-written Harmony prefixes shaped exactly like
    /// the shims a hot reload transform would generate (static methods binding __instance and
    /// ref __result by name, returning false to skip the original) fully replace method bodies
    /// on the Editor Mono runtime, across the method shapes the design promises: instance void,
    /// instance with return value, static, async, iterator, and private methods, including
    /// delegates captured before the patch.
    /// </summary>
    public class HotReloadSpikeS3PrefixDelegationTests
    {
        // Test-scoped id: sharing the production hot reload id would let this suite's
        // UnpatchAll remove real hot reload patches from the Editor domain.
        private const string HarmonyId = "io.github.hatayama.uloop.hot-reload-spike-s3";

        private class SpikeDelegationTarget
        {
            public int Offset = 3;
            public List<string> CallLog = new();

            public void RecordGreeting()
            {
                CallLog.Add("original");
            }

            public int AddOffset(int value)
            {
                return value + Offset;
            }

            public static int StaticDouble(int value)
            {
                return value * 2;
            }

            public async Task<int> ComputeAsync()
            {
                await Task.Yield();
                return 1;
            }

            public IEnumerable<int> EnumerateNumbers()
            {
                yield return 1;
                yield return 2;
            }

            public int CallSecretNumber()
            {
                return SecretNumber();
            }

            private int SecretNumber()
            {
                return 5;
            }
        }

        // Harmony's prefix contract binds __instance/__result by parameter name and requires
        // ref for __result; the repo-wide no-ref rule yields to that external contract here,
        // exactly as the generated production shims will.
        private static class SpikeDelegationShims
        {
            public static bool RecordGreetingPrefix(SpikeDelegationTarget __instance)
            {
                __instance.CallLog.Add("replaced");
                return false;
            }

            public static bool AddOffsetPrefix(SpikeDelegationTarget __instance, ref int __result, int value)
            {
                __result = value * 10 + __instance.Offset;
                return false;
            }

            public static bool StaticDoublePrefix(ref int __result, int value)
            {
                __result = value * 100;
                return false;
            }

            public static bool ComputeAsyncPrefix(ref Task<int> __result)
            {
                __result = ReplacementComputeAsync();
                return false;
            }

            private static async Task<int> ReplacementComputeAsync()
            {
                await Task.Yield();
                return 42;
            }

            public static bool EnumerateNumbersPrefix(ref IEnumerable<int> __result)
            {
                __result = ReplacementNumbers();
                return false;
            }

            private static IEnumerable<int> ReplacementNumbers()
            {
                yield return 7;
                yield return 8;
                yield return 9;
            }

            public static bool SecretNumberPrefix(ref int __result)
            {
                __result = 55;
                return false;
            }
        }

        [TearDown]
        public void TearDown()
        {
            new Harmony(HarmonyId).UnpatchAll(HarmonyId);
        }

        /// <summary>What: a skipping prefix fully replaces an instance void method body.</summary>
        [Test]
        public void InstanceVoidMethod_WithSkippingPrefix_RunsReplacementBodyOnly()
        {
            PatchWithPrefix(nameof(SpikeDelegationTarget.RecordGreeting), nameof(SpikeDelegationShims.RecordGreetingPrefix));

            SpikeDelegationTarget target = new();
            target.RecordGreeting();

            Assert.That(target.CallLog, Is.EqualTo(new[] { "replaced" }));
        }

        /// <summary>
        /// What: a skipping prefix replaces the return value of an instance method and can read
        /// instance state through __instance, proving instance binding.
        /// </summary>
        [Test]
        public void InstanceMethodWithReturn_WithSkippingPrefix_UsesArgumentAndInstanceState()
        {
            PatchWithPrefix(nameof(SpikeDelegationTarget.AddOffset), nameof(SpikeDelegationShims.AddOffsetPrefix));

            SpikeDelegationTarget target = new();
            Assert.That(target.AddOffset(4), Is.EqualTo(43));
        }

        /// <summary>What: a skipping prefix fully replaces a static method.</summary>
        [Test]
        public void StaticMethod_WithSkippingPrefix_ReturnsReplacementValue()
        {
            PatchWithPrefix(nameof(SpikeDelegationTarget.StaticDouble), nameof(SpikeDelegationShims.StaticDoublePrefix));

            Assert.That(SpikeDelegationTarget.StaticDouble(4), Is.EqualTo(400));
        }

        /// <summary>
        /// What: a skipping prefix replaces an async method by substituting the returned task
        /// with one produced by a replacement async implementation.
        /// </summary>
        [Test]
        public async Task AsyncMethod_WithSkippingPrefix_AwaitsReplacementTask()
        {
            PatchWithPrefix(nameof(SpikeDelegationTarget.ComputeAsync), nameof(SpikeDelegationShims.ComputeAsyncPrefix));

            SpikeDelegationTarget target = new();
            int computedValue = await target.ComputeAsync();

            Assert.That(computedValue, Is.EqualTo(42));
        }

        /// <summary>
        /// What: a skipping prefix replaces an iterator method by substituting the returned
        /// sequence with one produced by a replacement iterator.
        /// </summary>
        [Test]
        public void IteratorMethod_WithSkippingPrefix_YieldsReplacementSequence()
        {
            PatchWithPrefix(nameof(SpikeDelegationTarget.EnumerateNumbers), nameof(SpikeDelegationShims.EnumerateNumbersPrefix));

            SpikeDelegationTarget target = new();
            List<int> observedValues = new(target.EnumerateNumbers());

            Assert.That(observedValues, Is.EqualTo(new[] { 7, 8, 9 }));
        }

        /// <summary>
        /// What: a delegate captured before the patch still hits the detour afterwards, because
        /// the delegate points at the method entry that Harmony rewrites in place.
        /// </summary>
        [Test]
        public void DelegateCapturedBeforePatch_AfterPatch_HitsDetour()
        {
            SpikeDelegationTarget target = new();
            Func<int, int> capturedDelegate = target.AddOffset;
            Assert.That(capturedDelegate(4), Is.EqualTo(7), "Sanity check before patching: 4 + Offset(3).");

            PatchWithPrefix(nameof(SpikeDelegationTarget.AddOffset), nameof(SpikeDelegationShims.AddOffsetPrefix));

            Assert.That(capturedDelegate(4), Is.EqualTo(43));
        }

        /// <summary>
        /// What: a private method resolved via AccessTools can be patched, observed both through
        /// a bound delegate and through its public caller compiled before the patch.
        /// </summary>
        [Test]
        public void PrivateMethod_PatchedViaAccessTools_ReturnsReplacementValue()
        {
            PatchWithPrefix("SecretNumber", nameof(SpikeDelegationShims.SecretNumberPrefix));

            SpikeDelegationTarget target = new();
            MethodInfo secretMethod = AccessTools.Method(typeof(SpikeDelegationTarget), "SecretNumber");
            Func<int> boundSecret = (Func<int>)secretMethod.CreateDelegate(typeof(Func<int>), target);
            Assert.That(boundSecret(), Is.EqualTo(55));

            // A failure on this line alone would mean the JIT inlined SecretNumber into its
            // caller — the documented IsLikelyJitInlined limitation — and must be recorded as a
            // spike finding rather than silently worked around.
            Assert.That(target.CallSecretNumber(), Is.EqualTo(55));
        }

        /// <summary>What: UnpatchAll removes the prefix and restores the original behavior.</summary>
        [Test]
        public void UnpatchAll_AfterPatch_RestoresOriginalBehavior()
        {
            PatchWithPrefix(nameof(SpikeDelegationTarget.AddOffset), nameof(SpikeDelegationShims.AddOffsetPrefix));
            SpikeDelegationTarget target = new();
            Assert.That(target.AddOffset(4), Is.EqualTo(43), "Sanity check: the patch must be active before unpatching.");

            new Harmony(HarmonyId).UnpatchAll(HarmonyId);

            Assert.That(target.AddOffset(4), Is.EqualTo(7));
        }

        private static void PatchWithPrefix(string targetMethodName, string prefixMethodName)
        {
            MethodInfo targetMethod = AccessTools.Method(typeof(SpikeDelegationTarget), targetMethodName);
            MethodInfo prefixMethod = AccessTools.Method(typeof(SpikeDelegationShims), prefixMethodName);
            Assert.That(targetMethod, Is.Not.Null, $"Target method not found: {targetMethodName}");
            Assert.That(prefixMethod, Is.Not.Null, $"Prefix method not found: {prefixMethodName}");

            Harmony harmony = new(HarmonyId);
            harmony.Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod));
        }
    }
}
