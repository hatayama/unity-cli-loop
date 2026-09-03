using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;

using HarmonyLib;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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
        private Func<MethodBase, MethodBase> _previousGetActiveShim;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
            _previousGetActiveShim = HotReloadPausePointCoordination.GetActiveShimForMethod;
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPausePointCoordination.GetActiveShimForMethod = _previousGetActiveShim;
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        [Test]
        public void Patch_InstanceMethod_CapturesLocalsParametersAndInstanceFieldOnHit()
        {
            // Verifies the base case: an instance method's parameters, its as-yet-unassigned local,
            // the synthetic "this" entry, and its declaring instance's own field are all captured
            // at the resolved statement.
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
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "left", "right", "sum", "this", "Tag" }));
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
            // pointer for a struct instance method, which must be dereferenced and boxed before
            // Capture, surfacing both the synthetic "this" entry and the boxed instance's field.
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
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "doubled", "this", "Value" }));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "Value").Value, Is.EqualTo("7"));
        }

        [Test]
        public void Patch_RefStructInstanceMethod_DegradesToNullInstanceAndCapturesLocalsWithWarning()
        {
            // Verifies the byref-like (ref struct) declaring-type degradation: boxing a ref
            // struct's `this` is illegal IL, so the injected instance load must fall back to a
            // null instance instead. Locals are still captured normally, the instance field
            // ("Value") is absent since there is no boxed instance to read it from, and Patch
            // reports a non-empty Warning explaining the degradation.
            const string id = "patcher-ref-struct-instance-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherRefStructInstanceMethodFixture.cs", 11);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Is.Not.Empty);

            PatcherRefStructInstanceMethodFixture fixture = new() { Value = 5 };
            int doubled = fixture.Double();

            Assert.That(doubled, Is.EqualTo(10));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "doubled" }));
        }

        [Test]
        public void Patch_PhysicsMessageMethodOnMonoBehaviour_ReturnsCachedDispatchWarning()
        {
            // Verifies OnCollisionEnter2D on a MonoBehaviour-derived type reports the informational
            // warning about Unity's physics message dispatch caching its call path independently of
            // this patch (see To-Do 2 investigation: an already-existing GameObject's collision
            // callback can miss the pause point even though the method body runs). This fixture's
            // body is also small enough to additionally trigger the inlining-risk warning (see
            // Patch_PhysicsMessageMethodOnMonoBehaviour_AlsoTriggersInliningWarning for the exact
            // joined string), so this test only pins the physical-callback warning's presence.
            const string id = "patcher-physical-callback-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherPhysicalCallbackMethodFixture.cs", 13);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Does.Contain(SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning));
            Assert.That(patchResult.Warning, Does.Contain(SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning));
        }

        [Test]
        public void Patch_PhysicsMessageMethodOnMonoBehaviour_AlsoTriggersInliningWarning()
        {
            // Verifies that when the physical-callback warnings and the small-body inlining-risk
            // warning all apply to the same method (OnCollisionEnter2D's body is only 16 IL bytes,
            // well under SmallMethodInliningRiskThresholdBytes), BuildPatchWarning joins them in
            // the same order as the checks appear in its source (cached-dispatch first, then
            // mid-solver values, then inlining-risk), space-separated.
            const string id = "patcher-physical-callback-method-dual-warning";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherPhysicalCallbackMethodFixture.cs", 13);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Is.EqualTo(
                SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning + " "
                + SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning + " "
                + SourcePausePointConstants.SmallMethodInliningRiskWarning));
        }

        [Test]
        public void Patch_OrdinaryMessageMethodOnSameMonoBehaviour_DoesNotReturnCachedDispatchWarning()
        {
            // Verifies Update() on the same MonoBehaviour fixture does not trigger the
            // physics-message warning, since only physics message methods carry the caching risk.
            // Update()'s body is also small (16 IL bytes), so the inlining-risk warning is still
            // present; this test only asserts the physical-callback warning's absence.
            const string id = "patcher-ordinary-message-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherPhysicalCallbackMethodFixture.cs", 18);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Does.Not.Contain(SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning));
            Assert.That(patchResult.Warning, Does.Not.Contain(SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning));
        }

        [Test]
        public void Patch_HelperMethodCalledFromPhysicalCallback_ReturnsIndirectCachedDispatchWarning()
        {
            // Verifies a private helper method invoked (one level deep) from OnCollisionEnter2D on
            // the same MonoBehaviour reports the indirect cached-dispatch warning, extending the
            // direct-name check above (Patch_PhysicsMessageMethodOnMonoBehaviour_...) to methods
            // that are not themselves named after a physics message method.
            const string id = "patcher-physical-callback-helper-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherPhysicalCallbackHelperMethodFixture.cs", 18);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Does.Contain(SourcePausePointConstants.PhysicalCallbackIndirectCallMayMissExistingInstanceWarning));
            Assert.That(patchResult.Warning, Does.Contain(SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning));
        }

        [Test]
        public void Patch_HelperMethodNotCalledFromPhysicalCallback_DoesNotReturnIndirectCachedDispatchWarning()
        {
            // Verifies a sibling private method that no physical message method calls does not
            // trigger the indirect-call warning, proving the call-site scan does not over-match
            // every method declared on the same type.
            const string id = "patcher-physical-callback-unrelated-helper-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherPhysicalCallbackHelperMethodFixture.cs", 23);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Does.Not.Contain(SourcePausePointConstants.PhysicalCallbackIndirectCallMayMissExistingInstanceWarning));
            Assert.That(patchResult.Warning, Does.Not.Contain(SourcePausePointConstants.PhysicalCallbackMidSolverValuesWarning));
        }

        [Test]
        public void Patch_PhysicsNamedMethodOnNonMonoBehaviourType_DoesNotReturnCachedDispatchWarning()
        {
            // Verifies the warning requires both a matching method name AND a MonoBehaviour-derived
            // declaring type, so an unrelated plain class that happens to name a method
            // "OnTriggerEnter2D" does not produce a false positive. This fixture's body is also
            // small (16 IL bytes), so the inlining-risk warning is still present; this test only
            // asserts the physical-callback warning's absence.
            const string id = "patcher-physics-named-method-plain-class";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherPhysicsNamedMethodOnPlainClassFixture.cs", 9);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Does.Not.Contain(SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning));
        }

        [Test]
        public void Patch_SmallMethodBody_ReturnsInliningRiskWarning()
        {
            // Verifies a method whose IL body is at or under SmallMethodInliningRiskThresholdBytes
            // triggers the inlining-risk warning. The precondition assert guards against C# compiler
            // version drift silently moving this fixture's IL size to the wrong side of the threshold.
            byte[] ilBytes = typeof(PatcherStaticMethodFixture).GetMethod(nameof(PatcherStaticMethodFixture.Add))
                .GetMethodBody().GetILAsByteArray();
            Assert.That(ilBytes.Length, Is.LessThanOrEqualTo(SourcePausePointConstants.SmallMethodInliningRiskThresholdBytes),
                "Fixture precondition failed: PatcherStaticMethodFixture.Add must stay at or under the inlining-risk threshold.");

            const string id = "patcher-small-method-body";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherStaticMethodFixture.cs", 10);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Is.EqualTo(SourcePausePointConstants.SmallMethodInliningRiskWarning));
        }

        [Test]
        public void Patch_LargeMethodBody_DoesNotReturnInliningRiskWarning()
        {
            // Verifies a method whose IL body clearly exceeds SmallMethodInliningRiskThresholdBytes
            // (with a safety margin against compiler version drift, rather than sizing the fixture
            // to land just past the boundary) does not trigger the inlining-risk warning.
            byte[] ilBytes = typeof(PatcherLargeMethodFixture).GetMethod(nameof(PatcherLargeMethodFixture.Classify))
                .GetMethodBody().GetILAsByteArray();
            Assert.That(ilBytes.Length, Is.GreaterThan(SourcePausePointConstants.SmallMethodInliningRiskThresholdBytes + 8),
                "Fixture precondition failed: PatcherLargeMethodFixture.Classify must clearly exceed the inlining-risk threshold.");

            const string id = "patcher-large-method-body";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherLargeMethodFixture.cs", 23);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Is.Empty);
        }

        [Test]
        public void Patch_AggressiveInliningMethod_ReturnsInliningRiskWarningRegardlessOfSize()
        {
            // Verifies [MethodImpl(MethodImplOptions.AggressiveInlining)] triggers the inlining-risk
            // warning independent of IL body size: this fixture's body is identical in shape to
            // PatcherLargeMethodFixture (clearly over the threshold), so the warning here can only
            // come from the attribute check, not the size check.
            byte[] ilBytes = typeof(PatcherAggressiveInliningMethodFixture).GetMethod(nameof(PatcherAggressiveInliningMethodFixture.Classify))
                .GetMethodBody().GetILAsByteArray();
            Assert.That(ilBytes.Length, Is.GreaterThan(SourcePausePointConstants.SmallMethodInliningRiskThresholdBytes + 8),
                "Fixture precondition failed: PatcherAggressiveInliningMethodFixture.Classify must clearly exceed the inlining-risk threshold so this test isolates the attribute check.");

            const string id = "patcher-aggressive-inlining-method";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherAggressiveInliningMethodFixture.cs", 26);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);

            Assert.That(patchResult.Success, Is.True);
            Assert.That(patchResult.Warning, Is.EqualTo(SourcePausePointConstants.SmallMethodInliningRiskWarning));
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

        /// <summary>
        /// Verifies an armed method entry is counted even when execution returns before its marker.
        /// </summary>
        [Test]
        public void Patch_WhenBranchSkipsArmedLine_RecordsMethodEntryWithoutHit()
        {
            const string id = "patcher-entry-branch-skip";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherMethodEntryCountFixture.cs", 11);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            int result = PatcherMethodEntryCountFixture.ReturnBeforeArmedLine(true);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(result, Is.EqualTo(-1));
            Assert.That(snapshot.HitCount, Is.EqualTo(0));
            Assert.That(snapshot.MethodEntryCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies a do-while back-edge targeting the original first instruction does not re-count entry.
        /// </summary>
        [Test]
        public void Patch_WhenLoopBackEdgeTargetsOriginalFirstInstruction_RecordsOneMethodEntry()
        {
            const string id = "patcher-entry-loop-back-edge";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherMethodEntryCountFixture.cs", 26);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            int result = PatcherMethodEntryCountFixture.CountDownBeforeArmedLine(4);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(result, Is.EqualTo(0));
            Assert.That(snapshot.HitCount, Is.EqualTo(0));
            Assert.That(snapshot.MethodEntryCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies independently armed markers in one method each receive its entry count.
        /// </summary>
        [Test]
        public void Patch_WhenTwoMarkersArmOneMethod_RecordsEachMethodEntryCount()
        {
            const string firstId = "patcher-entry-first-marker";
            const string secondId = "patcher-entry-second-marker";
            SourcePausePointResolveResult firstResolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherMethodEntryCountFixture.cs", 35);
            SourcePausePointResolveResult secondResolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherMethodEntryCountFixture.cs", 36);
            Assert.That(firstResolveResult.Success, Is.True);
            Assert.That(secondResolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(firstId, 30);
            UloopPausePointRegistry.Enable(secondId, 30);
            SourcePausePointPatchResult firstPatchResult = SourcePausePointPatcher.Patch(firstId, firstResolveResult.Resolution);
            SourcePausePointPatchResult secondPatchResult = SourcePausePointPatcher.Patch(secondId, secondResolveResult.Resolution);
            Assert.That(firstPatchResult.Success, Is.True);
            Assert.That(secondPatchResult.Success, Is.True);

            int result = PatcherMethodEntryCountFixture.AddWithTwoArmedLines(3);

            UloopPausePointSnapshot firstSnapshot = UloopPausePointRegistry.GetStatus(firstId);
            UloopPausePointSnapshot secondSnapshot = UloopPausePointRegistry.GetStatus(secondId);
            Assert.That(result, Is.EqualTo(7));
            Assert.That(firstSnapshot.MethodEntryCount, Is.EqualTo(1));
            Assert.That(secondSnapshot.MethodEntryCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies method entry counter insertion leaves the original first instruction's label and exception block in place.
        /// </summary>
        [Test]
        public void InsertMethodEntryCounters_WhenFirstInstructionOwnsControlFlowMetadata_DoesNotMoveMetadata()
        {
            DynamicMethod method = new DynamicMethod("CounterMetadata", null, Type.EmptyTypes);
            ILGenerator generator = method.GetILGenerator();
            Label originalLabel = generator.DefineLabel();
            ExceptionBlock originalBlock = new ExceptionBlock(ExceptionBlockType.BeginExceptionBlock);
            CodeInstruction originalFirstInstruction = new CodeInstruction(OpCodes.Nop);
            originalFirstInstruction.labels.Add(originalLabel);
            originalFirstInstruction.blocks.Add(originalBlock);
            List<CodeInstruction> instructions = new List<CodeInstruction>
            {
                originalFirstInstruction,
                new CodeInstruction(OpCodes.Ret),
            };

            SourcePausePointInjectionEmitter.InsertMethodEntryCounters(
                instructions,
                new List<string> { "first", "first", "second" });

            Assert.That(instructions[0].opcode, Is.EqualTo(OpCodes.Ldstr));
            Assert.That(instructions[0].operand, Is.EqualTo("second"));
            Assert.That(instructions[0].labels, Is.Empty);
            Assert.That(instructions[0].blocks, Is.Empty);
            Assert.That(instructions[1].opcode, Is.EqualTo(OpCodes.Call));
            Assert.That(instructions[1].labels, Is.Empty);
            Assert.That(instructions[1].blocks, Is.Empty);
            Assert.That(instructions[2].opcode, Is.EqualTo(OpCodes.Ldstr));
            Assert.That(instructions[2].operand, Is.EqualTo("first"));
            Assert.That(instructions[3].opcode, Is.EqualTo(OpCodes.Call));
            Assert.That(instructions[4], Is.SameAs(originalFirstInstruction));
            Assert.That(originalFirstInstruction.labels, Is.EquivalentTo(new[] { originalLabel }));
            Assert.That(originalFirstInstruction.blocks, Is.EquivalentTo(new[] { originalBlock }));
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
        public void Patch_NeverArmedId_SkipsCaptureArrayBuildAndExecutesNormally()
        {
            // Verifies the not-armed guard: when Patch is called without ever Enable-ing the id,
            // the injected IsArmed check short-circuits straight to the original instruction
            // (skipping the parameter/local array build and the Capture call entirely) and the
            // method's own result is unaffected.
            const string id = "patcher-never-armed";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherStaticMethodFixture.cs", 9);
            Assert.That(resolveResult.Success, Is.True);

            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            int sum = PatcherStaticMethodFixture.Add(4, 5);

            Assert.That(sum, Is.EqualTo(9));
            Assert.That(UloopPausePointRegistry.GetStatus(id).IsHit, Is.False);
        }

        [Test]
        public void Patch_TryFinallyMethod_AtExceptionRegionEndBoundary_ExecutesNormally()
        {
            // Verifies the displaced instruction case at the opposite exception-region boundary
            // from the existing try/finally test above: line 21 is the first instruction after
            // the whole try/finally construct, so it carries an end-of-region block marker
            // rather than a begin marker, and must move to the injected sequence the same way.
            const string id = "patcher-try-finally-end-boundary";
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(
                FixturesDirectory + "PatcherTryFinallyMethodFixture.cs", 21);
            Assert.That(resolveResult.Success, Is.True);

            UloopPausePointRegistry.Enable(id, 30);
            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True);

            int result = PatcherTryFinallyMethodFixture.Divide(10, 2);

            Assert.That(result, Is.EqualTo(5));
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "numerator", "denominator", "result" }));
            Assert.That(snapshot.CapturedVariables.First(v => v.Name == "result").Value, Is.EqualTo("5"));
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

        [Test]
        public void Patch_ResolutionWithStaleMvid_ReturnsStaleAssemblyFailure()
        {
            // Verifies a resolution taken from a since-recompiled assembly (simulated here by a
            // deliberately wrong Mvid) is rejected before ResolveMethod ever runs against a
            // metadata token that may no longer mean the same thing in the now-loaded assembly.
            MethodBase method = typeof(PatcherStaticMethodFixture).GetMethod(nameof(PatcherStaticMethodFixture.Add));
            SourcePausePointResolution staleResolution = BuildSyntheticResolutionWithMvid(method, Guid.NewGuid().ToString());

            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch("patcher-stale-assembly", staleResolution);

            Assert.That(result.Success, Is.False);
            Assert.That(result.FailureReason, Is.EqualTo(SourcePausePointPatchFailureReason.StaleAssembly));
            Assert.That(result.Hint, Is.Not.Empty);
        }

        /// <summary>
        /// What: a hot-reload patched method with no compiled span keeps the existing
        /// patched-by-hot-reload failure message.
        /// </summary>
        [Test]
        public void Patch_OnHotReloadedMethod_WithoutCompiledSpan_KeepsExistingMessage()
        {
            MethodBase method = typeof(PatcherStaticMethodFixture).GetMethod(nameof(PatcherStaticMethodFixture.Add));
            HotReloadPausePointCoordination.GetActiveShimForMethod = _ => method;
            const int requestedLine = 42;
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "patcher-hot-reload-no-span",
                BuildSyntheticResolution(method),
                requestedLine: requestedLine);

            Assert.That(result.Success, Is.False);
            Assert.That(
                result.FailureReason,
                Is.EqualTo(SourcePausePointPatchFailureReason.MethodPatchedByHotReload));
            Assert.That(
                result.ErrorMessage,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadPatchedLineOutsidePatchedBodyMessageFormat,
                        method.DeclaringType.Name,
                        method.Name,
                        requestedLine)));
        }

        /// <summary>
        /// What: a hot-reload patched method with a compiled span appends that span to the
        /// patched-by-hot-reload failure message.
        /// </summary>
        [Test]
        public void Patch_OnHotReloadedMethod_WithCompiledSpan_AppendsSpanSentence()
        {
            MethodBase method = typeof(PatcherStaticMethodFixture).GetMethod(nameof(PatcherStaticMethodFixture.Add));
            HotReloadPausePointCoordination.GetActiveShimForMethod = _ => method;
            const int requestedLine = 42;
            const int compiledStart = 10;
            const int compiledEnd = 20;
            SourcePausePointPatchResult result = SourcePausePointPatcher.Patch(
                "patcher-hot-reload-span",
                BuildSyntheticResolutionWithMvid(
                    method,
                    method.Module.ModuleVersionId.ToString(),
                    compiledStart,
                    compiledEnd),
                requestedLine: requestedLine);

            string expectedMessage =
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedLineOutsidePatchedBodyMessageFormat,
                    method.DeclaringType.Name,
                    method.Name,
                    requestedLine)
                + string.Format(
                    SourcePausePointConstants.HotReloadPatchedCompiledMethodSpanFormat,
                    method.DeclaringType.Name,
                    method.Name,
                    compiledStart,
                    compiledEnd);
            Assert.That(result.Success, Is.False);
            Assert.That(
                result.FailureReason,
                Is.EqualTo(SourcePausePointPatchFailureReason.MethodPatchedByHotReload));
            Assert.That(result.ErrorMessage, Is.EqualTo(expectedMessage));
        }

        // Builds a resolution good enough to reach SourcePausePointPatcher's patchability gate; the
        // instruction index and locals/parameters are never read because every case here fails before that.
        private static SourcePausePointResolution BuildSyntheticResolution(MethodBase method)
        {
            return BuildSyntheticResolutionWithMvid(method, method.Module.ModuleVersionId.ToString());
        }

        // Same as BuildSyntheticResolution but lets a test supply a deliberately wrong Mvid, to
        // exercise the stale-assembly gate without needing a second compiled assembly.
        private static SourcePausePointResolution BuildSyntheticResolutionWithMvid(
            MethodBase method,
            string mvid,
            int compiledMethodStartLine = 0,
            int compiledMethodEndLine = 0)
        {
            return new SourcePausePointResolution(
                method.Module.Assembly.GetName().Name,
                mvid,
                method.MetadataToken,
                method.ToString(),
                method.IsStatic,
                method.DeclaringType.IsValueType,
                0,
                0,
                SourcePausePointSnapshotTiming.PreLine,
                1,
                1,
                compiledMethodStartLine,
                compiledMethodEndLine,
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

            public void Resume()
            {
                // Why zero: Unity's isPaused is a bool; Option B Resume must fully clear pause.
                PauseCount = 0;
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
