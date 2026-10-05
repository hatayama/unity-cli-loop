using System;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that a pause point inside a local function captures the variables and the
    /// instance its enclosing scopes pass to it in by-ref closure structs, whichever shape the
    /// compiler gave the local function.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointClosureFrameCaptureTests
    {
        private const string FixturePath =
            "Assets/Tests/Editor/SourcePausePointPatcher/Fixtures/PatcherClosureFrameLocalFunctionFixture.cs";
        private const int LoopClosureLocalFunctionLine = 22;
        private const int InstanceLocalFunctionReturnLine = 37;
        private const int StaticLocalFunctionReturnLine = 49;
        private const int NestedScopesLocalFunctionReturnLine = 64;
        private const int ParameterNestedScopesLocalFunctionReturnLine = 82;

        private FakePausePointPauseController _pauseController;
        private HotReloadSidePortScope _hotReloadSideScope;

        [SetUp]
        public void SetUp()
        {
            _pauseController = new FakePausePointPauseController();
            UloopPausePointRegistry.ConfigureForTests(_pauseController, () => DateTime.UtcNow);
            _hotReloadSideScope = new HotReloadSidePortScope();
        }

        [TearDown]
        public void TearDown()
        {
            _hotReloadSideScope.Dispose();
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        [Test]
        public void LocalFunctionOfLoopClosureClass_CapturesThisAndEveryEnclosingScope()
        {
            // Verifies a local function compiled as an instance method of a loop closure class,
            // with the instance and the method parameters in a by-ref closure struct, captures
            // "this", the instance fields, the method parameter, and the loop variable.
            UloopPausePointSnapshot snapshot = EnableAndHit(
                "closure-frame-loop", LoopClosureLocalFunctionLine,
                () => Assert.That(new PatcherClosureFrameLocalFunctionFixture(10).SumInLoopClosure(1), Is.EqualTo(39)));

            AssertCaptured(snapshot, "this", UloopCapturedVariableScope.This);
            Assert.That(AssertCaptured(snapshot, "_value", UloopCapturedVariableScope.InstanceField).Value, Is.EqualTo("10"));
            Assert.That(AssertCaptured(snapshot, "delta", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("1"));
            Assert.That(AssertCaptured(snapshot, "j", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("0"));
        }

        [Test]
        public void LocalFunctionOfInstanceMethod_CapturesThisAndTheEnclosingLocals()
        {
            // Verifies a local function compiled as an instance method of the declaring class,
            // with the enclosing locals in a by-ref closure struct, captures those locals
            // alongside "this" and its own locals.
            UloopPausePointSnapshot snapshot = EnableAndHit(
                "closure-frame-instance", InstanceLocalFunctionReturnLine,
                () => Assert.That(new PatcherClosureFrameLocalFunctionFixture(10).SumWithoutClosureClass(1), Is.EqualTo(12)));

            AssertCaptured(snapshot, "this", UloopCapturedVariableScope.This);
            Assert.That(AssertCaptured(snapshot, "_value", UloopCapturedVariableScope.InstanceField).Value, Is.EqualTo("10"));
            Assert.That(AssertCaptured(snapshot, "offset", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("2"));
            Assert.That(AssertCaptured(snapshot, "sum", UloopCapturedVariableScope.Local).Value, Is.EqualTo("12"));
        }

        [Test]
        public void LocalFunctionOfStaticMethod_CapturesTheEnclosingLocalsWithoutThis()
        {
            // Verifies a static local function receiving only a by-ref closure struct still
            // captures the enclosing locals and reports no "this".
            UloopPausePointSnapshot snapshot = EnableAndHit(
                "closure-frame-static", StaticLocalFunctionReturnLine,
                () => Assert.That(PatcherClosureFrameLocalFunctionFixture.SumInStaticMethod(1), Is.EqualTo(3)));

            Assert.That(snapshot.CapturedVariables.Any(v => v.Name == "this"), Is.False, FormatCaptured(snapshot));
            Assert.That(AssertCaptured(snapshot, "offset", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("2"));
            Assert.That(AssertCaptured(snapshot, "sum", UloopCapturedVariableScope.Local).Value, Is.EqualTo("3"));
        }

        [Test]
        public void LocalFunctionAcrossNestedScopes_CapturesEveryClosureFrame()
        {
            // Verifies a static local function receiving one closure struct per enclosing scope
            // captures the variables of every scope, not only those of the first struct.
            UloopPausePointSnapshot snapshot = EnableAndHit(
                "closure-frame-nested", NestedScopesLocalFunctionReturnLine,
                () => Assert.That(PatcherClosureFrameLocalFunctionFixture.SumAcrossNestedScopes(1), Is.EqualTo(4)));

            Assert.That(snapshot.CapturedVariables.Any(v => v.Name == "this"), Is.False, FormatCaptured(snapshot));
            Assert.That(AssertCaptured(snapshot, "offset", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("2"));
            Assert.That(AssertCaptured(snapshot, "inner", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("2"));
            Assert.That(AssertCaptured(snapshot, "sum", UloopCapturedVariableScope.Local).Value, Is.EqualTo("4"));
        }

        [Test]
        public void LocalFunctionWithParameterAcrossNestedScopes_CapturesEveryFrameAndParameter()
        {
            // Verifies an instance local function whose closure structs follow its own declared
            // parameter captures that parameter and every struct, each read from its own argument.
            UloopPausePointSnapshot snapshot = EnableAndHit(
                "closure-frame-parameter", ParameterNestedScopesLocalFunctionReturnLine,
                () => Assert.That(
                    new PatcherClosureFrameLocalFunctionFixture(10).SumWithParameterAcrossNestedScopes(1),
                    Is.EqualTo(114)));

            AssertCaptured(snapshot, "this", UloopCapturedVariableScope.This);
            Assert.That(AssertCaptured(snapshot, "_value", UloopCapturedVariableScope.InstanceField).Value, Is.EqualTo("10"));
            Assert.That(AssertCaptured(snapshot, "extra", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("100"));
            Assert.That(AssertCaptured(snapshot, "offset", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("2"));
            Assert.That(AssertCaptured(snapshot, "inner", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("2"));
            Assert.That(AssertCaptured(snapshot, "sum", UloopCapturedVariableScope.Local).Value, Is.EqualTo("114"));
        }

        [Test]
        public void LocalFunctionAcrossNestedScopes_EvaluatesHitWhenOnFrameVariable()
        {
            // Verifies a hit-when condition on a variable that only a closure struct carries is
            // evaluated against its value: a non-matching call is skipped, a matching one hits.
            const string id = "closure-frame-hit-when";
            const string hitWhen = "inner == 3";
            UloopPausePointHitWhenParseResult parseResult = UloopPausePointHitWhenCondition.Parse(hitWhen);
            Assert.That(parseResult.Condition, Is.Not.Null, parseResult.ErrorMessage);
            UloopPausePointRegistry.Enable(
                id,
                30,
                UloopPausePointCaptureMode.SingleShot,
                UloopPausePointRegistry.DefaultMaxHistory,
                UloopPausePointRegistry.DefaultMaxPreviewElements,
                UloopPausePointRegistry.DefaultMaxCallerFrames,
                hitWhen,
                parseResult.Condition);
            ResolveAndPatch(id, ParameterNestedScopesLocalFunctionReturnLine);
            PatcherClosureFrameLocalFunctionFixture fixture = new PatcherClosureFrameLocalFunctionFixture(10);

            fixture.SumWithParameterAcrossNestedScopes(1);

            UloopPausePointSnapshot skipped = UloopPausePointRegistry.GetStatus(id);
            Assert.That(skipped.IsHit, Is.False, skipped.HitWhenErrorNote);
            Assert.That(skipped.HitWhenSkippedCount, Is.EqualTo(1));
            Assert.That(skipped.HitWhenErrorNote, Is.Empty);

            fixture.SumWithParameterAcrossNestedScopes(2);

            UloopPausePointSnapshot hit = UloopPausePointRegistry.GetStatus(id);
            Assert.That(hit.IsHit, Is.True);
            Assert.That(AssertCaptured(hit, "inner", UloopCapturedVariableScope.Parameter).Value, Is.EqualTo("3"));
        }

        [TestCase(LoopClosureLocalFunctionLine)]
        [TestCase(InstanceLocalFunctionReturnLine)]
        [TestCase(StaticLocalFunctionReturnLine)]
        [TestCase(NestedScopesLocalFunctionReturnLine)]
        [TestCase(ParameterNestedScopesLocalFunctionReturnLine)]
        public void Resolve_InLocalFunctionTakingClosureStructs_ReportsNoNotCapturableParameter(int line)
        {
            // Verifies a closure struct argument, whose variables are captured through it, is not
            // also reported as a ref parameter that cannot be captured.
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(FixturePath, line);

            Assert.That(resolveResult.Success, Is.True, resolveResult.ErrorMessage);
            Assert.That(resolveResult.Resolution.NotCapturableVariables, Is.Empty);
        }

        private static UloopPausePointSnapshot EnableAndHit(string id, int line, Action invoke)
        {
            UloopPausePointRegistry.Enable(id, 30);
            ResolveAndPatch(id, line);

            invoke();

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus(id);
            Assert.That(snapshot.IsHit, Is.True);
            return snapshot;
        }

        private static void ResolveAndPatch(string id, int line)
        {
            SourcePausePointResolveResult resolveResult = SourcePausePointResolver.Resolve(FixturePath, line);
            Assert.That(resolveResult.Success, Is.True, resolveResult.ErrorMessage);
            Assert.That(resolveResult.Resolution.ResolvedLine, Is.EqualTo(line));

            SourcePausePointPatchResult patchResult = SourcePausePointPatcher.Patch(id, resolveResult.Resolution);
            Assert.That(patchResult.Success, Is.True, patchResult.ErrorMessage);
        }

        private static UloopCapturedVariable AssertCaptured(UloopPausePointSnapshot snapshot, string name, string scope)
        {
            UloopCapturedVariable variable = snapshot.CapturedVariables.FirstOrDefault(v => v.Name == name);
            Assert.That(variable, Is.Not.Null, $"'{name}' was not captured: {FormatCaptured(snapshot)}");
            Assert.That(variable.Scope, Is.EqualTo(scope), FormatCaptured(snapshot));
            return variable;
        }

        private static string FormatCaptured(UloopPausePointSnapshot snapshot)
        {
            return string.Join(", ", snapshot.CapturedVariables.Select(v => $"{v.Name}({v.Scope})={v.Value}"));
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
}
