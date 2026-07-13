using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the Harmony-injected landing point: the armed fast-path no-op and the
    /// formatted-variables handoff into the registry.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointCaptureTests
    {
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
            UloopPausePointRegistry.ResetForTests();
        }

        [Test]
        public void Capture_WhenPausePointIsEnabled_RecordsFormattedVariablesInSnapshot()
        {
            // Verifies an armed marker's hit threads formatted locals/parameters into the snapshot.
            UloopPausePointRegistry.Enable("jump", 30);
            object[] parameters = { "damage", 3 };
            object[] locals = { "speed", 5 };

            SourcePausePointCapture.Capture("jump", null, parameters, locals);

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            Assert.That(snapshot.IsHit, Is.True);
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "speed", "damage" }));
            Assert.That(snapshot.CapturedVariablesTruncated, Is.False);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
        }

        [Test]
        public void Capture_WhenPausePointIsNotArmed_DoesNotPauseOrRecordAHit()
        {
            // Verifies the IsArmed fast path no-ops when the marker was never enabled.
            SourcePausePointCapture.Capture("never-enabled", null, Array.Empty<object>(), Array.Empty<object>());

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("never-enabled");
            Assert.That(snapshot.IsHit, Is.False);
            Assert.That(_pauseController.PauseCount, Is.EqualTo(0));
        }

        [Test]
        public void Capture_WhenPausePointWasAlreadyHit_IgnoresSecondCall()
        {
            // Verifies a one-shot marker disarms itself so a second pass through the same line no-ops.
            UloopPausePointRegistry.Enable("jump", 30);
            SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), Array.Empty<object>());

            SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), Array.Empty<object>());

            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies the source capture path records every hit for an armed continuous marker.
        /// </summary>
        [Test]
        public void Capture_WhenContinuousPausePointIsEnabled_RecordsEveryFormattedHit()
        {
            UloopPausePointRegistry.Enable("jump", 30, UloopPausePointCaptureMode.Continuous, 20);

            SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), new object[] { "speed", 1 });
            SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), new object[] { "speed", 2 });

            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");

            Assert.That(snapshot.IsEnabled, Is.True);
            Assert.That(snapshot.HitCount, Is.EqualTo(2));
            Assert.That(snapshot.CapturedVariableHistory, Has.Count.EqualTo(2));
            Assert.That(snapshot.CapturedVariables.Single(variable => variable.Name == "speed").Value, Is.EqualTo("2"));
            Assert.That(_pauseController.PauseCount, Is.EqualTo(2));
        }

        [UnityTest]
        public IEnumerator Capture_WhenCalledOffMainThread_RecordsHitOnNextMainThreadTick()
        {
            // Verifies an off-main-thread Capture call is marshalled to the main thread
            // (must-fix 2): EditorApplication.isPaused and the registry's own bookkeeping are
            // main-thread-only, so the hit must land via MainThreadSwitcher's continuation queue
            // rather than running inline on the calling background thread.
            UloopPausePointRegistry.Enable("jump", 30);
            object[] locals = { "speed", 5 };

            Task.Run(() => SourcePausePointCapture.Capture("jump", null, Array.Empty<object>(), locals));

            float timeoutTime = Time.realtimeSinceStartup + 5f;
            UloopPausePointSnapshot snapshot = UloopPausePointRegistry.GetStatus("jump");
            while (!snapshot.IsHit && Time.realtimeSinceStartup < timeoutTime)
            {
                yield return null;
                snapshot = UloopPausePointRegistry.GetStatus("jump");
            }

            Assert.That(snapshot.IsHit, Is.True, "hit should be recorded on the main thread within timeout");
            Assert.That(snapshot.CapturedVariables.Select(v => v.Name), Is.EquivalentTo(new[] { "speed" }));
            Assert.That(_pauseController.PauseCount, Is.EqualTo(1));
        }

        [Test]
        public void Collect_WithNormalInstance_AddsThisEntryReferencingTheInstance()
        {
            // Verifies a non-state-machine instance yields a synthetic "this" entry (Scope=This)
            // whose raw value is the paused instance itself.
            NormalInstanceFixture instance = new();

            UloopPausePointCapturedVariableFrame frame = SourcePausePointVariableCollector.Collect(
                instance, Array.Empty<object>(), Array.Empty<object>());

            UloopPausePointCapturedVariableEntry thisEntry = frame.Entries.Single(entry => entry.Name == "this");
            Assert.That(thisEntry.Scope, Is.EqualTo(UloopCapturedVariableScope.This));
            Assert.That(thisEntry.Value, Is.SameAs(instance));
        }

        [Test]
        public void Collect_OrdersThisAfterLocalsAndParametersButBeforeInstanceFields()
        {
            // Verifies the "this" entry lands after locals/parameters and before instance fields,
            // so the count cap keeps prioritizing locals and parameters.
            NormalInstanceFixture instance = new() { Health = 7 };
            object[] locals = { "speed", 5 };
            object[] parameters = { "damage", 3 };

            UloopPausePointCapturedVariableFrame frame = SourcePausePointVariableCollector.Collect(
                instance, parameters, locals);

            List<string> names = frame.Entries.Select(entry => entry.Name).ToList();
            int thisIndex = names.IndexOf("this");
            Assert.That(names.IndexOf("speed"), Is.LessThan(thisIndex));
            Assert.That(names.IndexOf("damage"), Is.LessThan(thisIndex));
            Assert.That(thisIndex, Is.LessThan(names.IndexOf("Health")));
        }

        [Test]
        public void Collect_WithStateMachineInstance_ResolvesThisToOuterInstanceNotStateMachine()
        {
            // Verifies an async/coroutine state machine emits the hoisted outer instance as "this"
            // and never surfaces the compiler-generated state machine object itself as "this".
            AsyncStateMachineFixture outer = new();
            (object stateMachine, Type stateMachineType) = CreateStateMachine();
            FieldInfo outerThisField = stateMachineType.GetField(
                "<>4__this", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            Assert.That(outerThisField, Is.Not.Null, "compiler must hoist <>4__this for this fixture");
            outerThisField.SetValue(stateMachine, outer);

            UloopPausePointCapturedVariableFrame frame = SourcePausePointVariableCollector.Collect(
                stateMachine, Array.Empty<object>(), Array.Empty<object>());

            UloopPausePointCapturedVariableEntry thisEntry = frame.Entries.Single(entry => entry.Name == "this");
            Assert.That(thisEntry.Scope, Is.EqualTo(UloopCapturedVariableScope.This));
            Assert.That(thisEntry.Value, Is.SameAs(outer));
            Assert.That(frame.Entries.Any(entry => ReferenceEquals(entry.Value, stateMachine)), Is.False);
        }

        [Test]
        public void Collect_WithStateMachineInstanceMissingOuterThis_AddsNoThisEntry()
        {
            // Verifies a state machine with a null hoisted outer instance emits no "this" entry,
            // rather than falling back to the state machine object itself.
            (object stateMachine, _) = CreateStateMachine();

            UloopPausePointCapturedVariableFrame frame = SourcePausePointVariableCollector.Collect(
                stateMachine, Array.Empty<object>(), Array.Empty<object>());

            Assert.That(frame.Entries.Any(entry => entry.Name == "this"), Is.False);
        }

        [Test]
        public void Collect_WithNullInstance_AddsNoThisEntry()
        {
            // Verifies a static method (null instance) produces no "this" entry.
            object[] locals = { "speed", 5 };

            UloopPausePointCapturedVariableFrame frame = SourcePausePointVariableCollector.Collect(
                null, Array.Empty<object>(), locals);

            Assert.That(frame.Entries.Any(entry => entry.Name == "this"), Is.False);
        }

        [Test]
        public void Collect_WhenCountCapReachedBeforeThis_OmitsThisAndReportsTruncated()
        {
            // Verifies that when locals already fill the count cap, the "this" entry is dropped and
            // truncation is reported per the existing TryAppendEntry contract.
            int localCount = SourcePausePointConstants.MaxCapturedVariableCount;
            object[] locals = new object[localCount * 2];
            for (int i = 0; i < localCount; i++)
            {
                locals[i * 2] = $"local{i}";
                locals[i * 2 + 1] = i;
            }

            NormalInstanceFixture instance = new();

            UloopPausePointCapturedVariableFrame frame = SourcePausePointVariableCollector.Collect(
                instance, Array.Empty<object>(), locals);

            Assert.That(frame.Entries.Count, Is.EqualTo(SourcePausePointConstants.MaxCapturedVariableCount));
            Assert.That(frame.Entries.Any(entry => entry.Name == "this"), Is.False);
            Assert.That(frame.Truncated, Is.True);
        }

        private static (object StateMachine, Type StateMachineType) CreateStateMachine()
        {
            Type stateMachineType = typeof(AsyncStateMachineFixture)
                .GetNestedTypes(BindingFlags.NonPublic)
                .Single(type => type.Name.StartsWith("<RunAsync>d__", StringComparison.Ordinal));
            object stateMachine = Activator.CreateInstance(stateMachineType);
            return (stateMachine, stateMachineType);
        }

        private sealed class NormalInstanceFixture
        {
            public int Health;
        }

        private sealed class AsyncStateMachineFixture
        {
            public int OuterField;

            public async Task<int> RunAsync(int seed)
            {
                int localValue = seed * 2;
                await Task.Yield();
                OuterField += localValue;
                return localValue;
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
