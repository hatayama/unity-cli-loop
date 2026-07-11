using System;
using System.Linq;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Resolver -> Patcher -> Capture -> Registry pipeline end-to-end against real
    /// compiled fixture methods, proving the resolved instruction index and IL argument/local
    /// indexing line up correctly across instance, static, value-type, loop, and try/finally shapes.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointPatcherTests
    {
        private const string FixturesDirectory = "Assets/Tests/Editor/SourcePausePointPatcher/Fixtures/";

        private FakePausePointPauseController _pauseController;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        [Test]
        public void Patch_InstanceMethod_CapturesLocalsParametersAndInstanceFieldOnHit()
        {
            // Verifies the base case: an instance method's parameters, its as-yet-unassigned local,
            // and its declaring instance's own field are all captured at the resolved statement.
            const string id = "patcher-normal-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherNormalMethodFixture.cs", 11);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            PatcherNormalMethodFixture fixture = new();
            int sum = fixture.Add(2, 3);

            Assert.That(sum, Is.EqualTo(5));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "left", "right", "sum", "Tag" }));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "left").Value, Is.EqualTo("2"));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "right").Value, Is.EqualTo("3"));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "sum").Value, Is.EqualTo("0"));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "Tag").Value, Is.EqualTo("fixture-instance"));
        }

        [Test]
        public void Patch_StaticMethod_PassesNullInstanceAndUsesUnshiftedArgumentIndices()
        {
            // Verifies static-method IL argument indexing has no `this`-slot offset, unlike the instance case above.
            const string id = "patcher-static-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherStaticMethodFixture.cs", 9);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            int sum = PatcherStaticMethodFixture.Add(10, 20);

            Assert.That(sum, Is.EqualTo(30));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "left", "right", "sum" }));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "left").Value, Is.EqualTo("10"));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "right").Value, Is.EqualTo("20"));
        }

        [Test]
        public void Patch_StructInstanceMethod_BoxesValueTypeThisAndCapturesInstanceField()
        {
            // Verifies the ldobj+box path used when `this` is a value type: ldarg.0 yields a managed
            // pointer for a struct instance method, which must be dereferenced and boxed before Capture.
            const string id = "patcher-struct-instance-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherStructInstanceMethodFixture.cs", 11);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            PatcherStructInstanceMethodFixture fixture = new() { Value = 7 };
            int doubled = fixture.Double();

            Assert.That(doubled, Is.EqualTo(14));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "doubled", "Value" }));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "Value").Value, Is.EqualTo("7"));
        }

        [Test]
        public void Patch_LoopMethod_PreservesBackEdgeBranchTargetAndCapturesFirstIterationState()
        {
            // Verifies CodeInstruction.labels are moved to the injected sequence's first instruction
            // when the insertion point is a loop's back-edge branch target, so the loop still runs
            // correctly; the pause point auto-disarms after its first hit, so only i=0/total=0 is seen.
            const string id = "patcher-loop-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherLoopMethodFixture.cs", 12);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            int total = PatcherLoopMethodFixture.SumUpTo(4);

            Assert.That(total, Is.EqualTo(6));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.HitCount, Is.EqualTo(1));
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "i", "total", "count" }));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "i").Value, Is.EqualTo("0"));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "total").Value, Is.EqualTo("0"));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "count").Value, Is.EqualTo("4"));
        }

        [Test]
        public void Patch_TryFinallyMethod_PreservesExceptionRegionBoundaryAndExecutesNormally()
        {
            // Verifies CodeInstruction.blocks are moved to the injected sequence's first instruction
            // when the insertion point is the first instruction inside a try block, so the exception
            // region still starts in the right place and the patched method still executes correctly.
            const string id = "patcher-try-finally-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherTryFinallyMethodFixture.cs", 14);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            int result = PatcherTryFinallyMethodFixture.Divide(10, 2);

            Assert.That(result, Is.EqualTo(5));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "numerator", "denominator", "result" }));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "numerator").Value, Is.EqualTo("10"));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "denominator").Value, Is.EqualTo("2"));
        }

        [Test]
        public void Patch_TwoPausePointsInSameMethod_BothHitIndependentlyWithCorrectState()
        {
            // Verifies multiple injections into the same method insert correctly regardless of
            // instruction order, each capturing the local's value at its own point in execution.
            const string idBeforeAssignment = "patcher-multi-before-assignment";
            const string idBeforeReturn = "patcher-multi-before-return";

            SourcePausePointResolveResult beforeAssignment = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherNormalMethodFixture.cs", 11);
            SourcePausePointResolveResult beforeReturn = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherNormalMethodFixture.cs", 12);
            Assert.That(beforeAssignment.Success, Is.True);
            Assert.That(beforeReturn.Success, Is.True);

            UloopPausePointRegistry.Enable(idBeforeAssignment, 30);
            UloopPausePointRegistry.Enable(idBeforeReturn, 30);
            Assert.That(SourcePausePointPatcher.Patch(idBeforeAssignment, beforeAssignment.Resolution).Success, Is.True);
            Assert.That(SourcePausePointPatcher.Patch(idBeforeReturn, beforeReturn.Resolution).Success, Is.True);

            PatcherNormalMethodFixture fixture = new();
            int sum = fixture.Add(2, 3);

            Assert.That(sum, Is.EqualTo(5));
            UloopPausePointSnapshot beforeAssignmentSnapshot = UloopPausePointRegistry.GetStatus(idBeforeAssignment);
            UloopPausePointSnapshot beforeReturnSnapshot = UloopPausePointRegistry.GetStatus(idBeforeReturn);
            Assert.That(beforeAssignmentSnapshot.IsHit, Is.True);
            Assert.That(beforeAssignmentSnapshot.CapturedVariables.First(v => v.Name == "sum").Value, Is.EqualTo("0"));
            Assert.That(beforeReturnSnapshot.IsHit, Is.True);
            Assert.That(beforeReturnSnapshot.CapturedVariables.First(v => v.Name == "sum").Value, Is.EqualTo("5"));
        }

        [Test]
        public void Patch_SameIdPatchedTwice_IsIdempotentAndStillHits()
        {
            // Verifies re-patching the same already-patched id is a no-op per Patch's documented
            // contract, and the already-injected call site still fires correctly.
            const string id = "patcher-idempotent";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherStaticMethodFixture.cs", 9);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);

            int sum = PatcherStaticMethodFixture.Add(1, 1);

            Assert.That(sum, Is.EqualTo(2));
            Assert.That(UloopPausePointRegistry.GetStatus(id).IsHit, Is.True);
        }

        [Test]
        public void Unpatch_ThenRepatch_RestoresOriginalBehaviorThenCapturesAgain()
        {
            // Verifies Unpatch removes the injected call site (no more capture/hit) and a
            // subsequent Patch with the same id re-injects it correctly.
            const string id = "patcher-unpatch-repatch";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherStaticMethodFixture.cs", 9);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);
            SourcePausePointPatcher.Unpatch(id);

            int sumWhileUnpatched = PatcherStaticMethodFixture.Add(4, 5);
            Assert.That(sumWhileUnpatched, Is.EqualTo(9));
            Assert.That(UloopPausePointRegistry.GetStatus(id).IsHit, Is.False);

            UloopPausePointRegistry.Enable(id, 30);
            Assert.That(SourcePausePointPatcher.Patch(id, resolveResult.Resolution).Success, Is.True);
            int sumAfterRepatch = PatcherStaticMethodFixture.Add(6, 7);

            Assert.That(sumAfterRepatch, Is.EqualTo(13));
            Assert.That(UloopPausePointRegistry.GetStatus(id).IsHit, Is.True);
        }

        [Test]
        public void Patch_AbstractMethod_ReturnsUnpatchableAbstractFailure()
        {
            // Verifies an abstract method (no method body to patch) is rejected before ever calling Harmony.Patch.
            MethodBase method = typeof(AbstractMethodFixture).GetMethod(nameof(AbstractMethodFixture.DoWork));
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "patcher-abstract-method", BuildSyntheticResolution(method));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.UnpatchableAbstract));
            Assert.That(result.Hint, Is.Not.Empty);
        }

        [Test]
        public void Patch_ExternMethod_ReturnsUnpatchableExternFailure()
        {
            // Verifies a method with no IL body (an internal call, the same shape a DllImport extern
            // method has) is rejected.
            MethodBase method = typeof(object).GetMethod("MemberwiseClone", BindingFlags.NonPublic | BindingFlags.Instance);
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "patcher-extern-method", BuildSyntheticResolution(method));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.UnpatchableExtern));
        }

        [Test]
        public void Patch_OpenGenericMethod_ReturnsUnpatchableOpenGenericFailure()
        {
            // Verifies a method with an unbound generic type parameter of its own is rejected.
            MethodBase method = typeof(GenericMethodFixture).GetMethod(nameof(GenericMethodFixture.DoWork));
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "patcher-open-generic-method", BuildSyntheticResolution(method));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.UnpatchableOpenGeneric));
        }

        [Test]
        public void Patch_NonGenericMethodInsideOpenGenericType_ReturnsUnpatchableOpenGenericFailure()
        {
            // Verifies a plain (non-generic) method declared inside an open generic type is also
            // rejected, since its declaring type's unbound T makes it just as unsafe to patch.
            MethodBase method = typeof(GenericTypeFixture<>).GetMethod(nameof(GenericTypeFixture<object>.PlainMethod));
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "patcher-open-generic-type", BuildSyntheticResolution(method));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.UnpatchableOpenGeneric));
        }

        [Test]
        public void Patch_BurstCompiledMethod_ReturnsUnpatchableBurstCompiledFailure()
        {
            // Verifies a method itself marked [BurstCompile] is rejected.
            MethodBase method = typeof(BurstMethodFixture).GetMethod(nameof(BurstMethodFixture.DoWork));
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "patcher-burst-method", BuildSyntheticResolution(method));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.UnpatchableBurstCompiled));
        }

        [Test]
        public void Patch_MethodInsideBurstCompiledType_ReturnsUnpatchableBurstCompiledFailure()
        {
            // Verifies a plain method whose declaring type is marked [BurstCompile] is also rejected,
            // mirroring how Unity's Burst-compiled job structs place the attribute on the struct, not the method.
            MethodBase method = typeof(BurstTypeFixture).GetMethod(nameof(BurstTypeFixture.Execute));
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "patcher-burst-type", BuildSyntheticResolution(method));

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.UnpatchableBurstCompiled));
        }

        // Builds a resolution good enough to reach SourcePausePointPatcher's patchability gate; the
        // instruction index and locals/parameters are never read because every case here fails before that.
        private static SourcePausePointResolution BuildSyntheticResolution(MethodBase method)
        {
            return new SourcePausePointResolution(
                method.Module.Assembly.GetName().Name,
                method.MetadataToken,
                method.ToString(),
                method.IsStatic,
                method.DeclaringType.IsValueType,
                0,
                0,
                1,
                Array.Empty<SourcePausePointLocalVariable>(),
                Array.Empty<SourcePausePointParameter>());
        }

        private abstract class AbstractMethodFixture
        {
            public abstract void DoWork();
        }

        private static class GenericMethodFixture
        {
            public static void DoWork<T>(T value)
            {
            }
        }

        private static class GenericTypeFixture<T>
        {
            public static void PlainMethod()
            {
            }
        }

        private static class BurstMethodFixture
        {
            [Unity.Burst.BurstCompile]
            public static void DoWork()
            {
            }
        }

        [Unity.Burst.BurstCompile]
        private struct BurstTypeFixture
        {
            public static void Execute()
            {
            }
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
        }
    }
}

namespace Unity.Burst
{
    // Test-only shadow of Unity.Burst.BurstCompileAttribute's FullName: this project does not
    // depend on com.unity.burst, and SourcePausePointPatcher only ever compares the FullName string.
    internal sealed class BurstCompileAttribute : Attribute
    {
    }
}
