using System;
using System.Linq;

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
