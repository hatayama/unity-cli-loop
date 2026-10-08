using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the warm-up facade: when it runs its items, how a run or a shutdown stops it,
    /// and what it reports. Items are stand-ins; an item held by a TaskCompletionSource stands
    /// for work in flight.
    /// </summary>
    public sealed class HotReloadWarmUpTests
    {
        private List<string> _ran;

        [SetUp]
        public void SetUp()
        {
            _ran = new List<string>();
            VibeLogger.ClearMemoryLogs();
        }

        [TearDown]
        public void TearDown()
        {
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a source that skips leaves every item unrun and the skip entry names its reason.
        /// </summary>
        [Test]
        public void Start_WhenTheSourceSaysSkip_RunsNoItemAndLogsTheReason()
        {
            HotReloadWarmUp warmUp = new HotReloadWarmUp(
                new FixedContextSource(HotReloadWarmUpCapture.Skipped(HotReloadWarmUpCapture.SkipReasonCompiling)),
                new IHotReloadWarmUpItem[] { new RecordingWarmUpItem("a", _ran) });

            warmUp.Start();

            Assert.That(_ran, Is.Empty, "items run");
            Assert.That(
                (string)HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogWarmUpSkipped)["reason"],
                Is.EqualTo(HotReloadWarmUpCapture.SkipReasonCompiling));
            Assert.That(warmUp.State, Is.EqualTo(HotReloadWarmUpState.Finished), "state");
        }

        /// <summary>
        /// What: with a context, every item runs once in order and the entry lists one done outcome per item.
        /// </summary>
        [Test]
        public async Task Start_RunsTheItemsInOrderAndLogsOneDoneOutcomePerItem()
        {
            HotReloadWarmUp warmUp = CreateReady(new RecordingWarmUpItem("a", _ran), new RecordingWarmUpItem("b", _ran));

            warmUp.Start();
            await warmUp.Completion;

            Assert.That(_ran, Is.EqualTo(new[] { "a", "b" }), "items run");
            Assert.That(HotReloadWarmUpTestDoubles.ReadCompletedOutcomes(), Is.EqualTo(new[] { "a:done", "b:done" }));
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogWarmUpComplete);
            Assert.That(context["targets"].ToObject<string[]>(), Is.EqualTo(new[] { HotReloadWarmUpTestDoubles.TargetAssemblyName }), "targets");
            Assert.That(context["cancelledBy"].Type, Is.EqualTo(JTokenType.Null), "cancelledBy");
        }

        /// <summary>
        /// What: a second Start runs nothing again.
        /// </summary>
        [Test]
        public async Task Start_Twice_RunsTheItemsOnce()
        {
            HotReloadWarmUp warmUp = CreateReady(new RecordingWarmUpItem("a", _ran));

            warmUp.Start();
            await warmUp.Completion;
            warmUp.Start();
            await warmUp.Completion;

            Assert.That(_ran, Is.EqualTo(new[] { "a" }));
        }

        /// <summary>
        /// What: a run that comes before the warm-up starts does not wait, and the warm-up then never starts.
        /// </summary>
        [Test]
        public void YieldToRunAsync_BeforeStart_CompletesAtOnce_AndStartAfterwardsRunsNothing()
        {
            HotReloadWarmUp warmUp = CreateReady(new RecordingWarmUpItem("a", _ran));

            Task yield = warmUp.YieldToRunAsync();
            warmUp.Start();

            Assert.That(yield.IsCompleted, Is.True, "yield completed");
            Assert.That(_ran, Is.Empty, "items run");
            Assert.That(
                (string)HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogWarmUpSkipped)["reason"],
                Is.EqualTo("run_started_first"));
        }

        /// <summary>
        /// What: a run that comes while an item is in flight waits for that item, and no later item starts.
        /// </summary>
        [Test]
        public async Task YieldToRunAsync_WhileAnItemIsInFlight_WaitsForThatItemAndStartsNoLaterOne()
        {
            PendingWarmUpItem pending = new PendingWarmUpItem("b", _ran);
            HotReloadWarmUp warmUp = CreateReady(
                new RecordingWarmUpItem("a", _ran),
                pending,
                new RecordingWarmUpItem("c", _ran));
            try
            {
                warmUp.Start();
                Assert.That(_ran, Is.EqualTo(new[] { "a", "b" }), "Precondition: item b must be in flight.");

                Task yield = warmUp.YieldToRunAsync();
                Assert.That(yield.IsCompleted, Is.False, "the run must wait while item b is in flight");

                pending.Release.TrySetResult(true);
                await yield;
            }
            finally
            {
                pending.Release.TrySetResult(true);
            }

            Assert.That(_ran, Is.EqualTo(new[] { "a", "b" }), "items run");
            Assert.That(
                HotReloadWarmUpTestDoubles.ReadCompletedOutcomes(),
                Is.EqualTo(new[] { "a:done", "b:done", "c:cancelled" }));
            Assert.That(
                (string)HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogWarmUpComplete)["cancelledBy"],
                Is.EqualTo(HotReloadConstants.WarmUpCancelledByRun));
        }

        /// <summary>
        /// What: a run that comes after the warm-up finished does not wait.
        /// </summary>
        [Test]
        public async Task YieldToRunAsync_AfterCompletion_CompletesAtOnce()
        {
            HotReloadWarmUp warmUp = CreateReady(new RecordingWarmUpItem("a", _ran));
            warmUp.Start();
            await warmUp.Completion;

            Task yield = warmUp.YieldToRunAsync();

            Assert.That(yield.IsCompleted, Is.True);
        }

        /// <summary>
        /// What: an item that fails with an IO error is reported failed with a warning, and the next item still runs.
        /// </summary>
        [Test]
        public async Task Start_WhenAnItemThrowsAnIoException_RecordsItFailedAndRunsTheNextItem()
        {
            HotReloadWarmUp warmUp = CreateReady(
                new ThrowingWarmUpItem("a", new IOException("locked")),
                new RecordingWarmUpItem("b", _ran));
            LogAssert.Expect(LogType.Warning, new Regex("warm-up item 'a' failed: locked"));

            warmUp.Start();
            await warmUp.Completion;

            Assert.That(_ran, Is.EqualTo(new[] { "b" }), "items run");
            Assert.That(HotReloadWarmUpTestDoubles.ReadCompletedOutcomes(), Is.EqualTo(new[] { "a:failed", "b:done" }));
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogWarmUpComplete);
            Assert.That((string)context["items"][0]["detail"], Is.EqualTo("IOException: locked"), "detail");
        }

        /// <summary>
        /// What: an unexpected exception stops the remaining items, is logged, and leaves the
        /// completion task unfaulted and the warm-up finished.
        /// </summary>
        [Test]
        public async Task Start_WhenAnItemThrowsAnUnexpectedException_LogsItAndCompletionDoesNotFault()
        {
            HotReloadWarmUp warmUp = CreateReady(
                new ThrowingWarmUpItem("a", new InvalidOperationException("unexpected")),
                new RecordingWarmUpItem("b", _ran));
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: unexpected"));

            warmUp.Start();
            await warmUp.Completion;

            Assert.That(_ran, Is.Empty, "items run");
            Assert.That(warmUp.Completion.IsFaulted, Is.False, "completion faulted");
            Assert.That(warmUp.State, Is.EqualTo(HotReloadWarmUpState.Finished), "state");
        }

        /// <summary>
        /// What: a shutdown while an item is in flight waits for that item, cancels the rest, and
        /// names its trigger in the entry.
        /// </summary>
        [Test]
        public async Task Shutdown_WhileAnItemIsInFlight_CancelsTheRemainingItemsAndRecordsTheTrigger()
        {
            PendingWarmUpItem pending = new PendingWarmUpItem("a", _ran);
            HotReloadWarmUp warmUp = CreateReady(pending, new RecordingWarmUpItem("b", _ran));
            try
            {
                warmUp.Start();

                Task stopped = warmUp.Shutdown("t");
                Assert.That(stopped.IsCompleted, Is.False, "the shutdown must wait while item a is in flight");

                pending.Release.TrySetResult(true);
                await stopped;
            }
            finally
            {
                pending.Release.TrySetResult(true);
            }

            Assert.That(_ran, Is.EqualTo(new[] { "a" }), "items run");
            Assert.That(HotReloadWarmUpTestDoubles.ReadCompletedOutcomes(), Is.EqualTo(new[] { "a:done", "b:cancelled" }));
            Assert.That(
                (string)HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogWarmUpComplete)["cancelledBy"],
                Is.EqualTo("t"));
        }

        /// <summary>
        /// What: a shutdown before the warm-up starts does nothing, and a later Start still runs the items.
        /// </summary>
        [Test]
        public async Task Shutdown_BeforeStart_DoesNothing_AndStartAfterwardsRuns()
        {
            HotReloadWarmUp warmUp = CreateReady(new RecordingWarmUpItem("a", _ran));

            Task stopped = warmUp.Shutdown("t");
            warmUp.Start();
            await warmUp.Completion;

            Assert.That(stopped.IsCompleted, Is.True, "shutdown completed");
            Assert.That(_ran, Is.EqualTo(new[] { "a" }), "items run");
        }

        private static HotReloadWarmUp CreateReady(params IHotReloadWarmUpItem[] items)
        {
            return new HotReloadWarmUp(new FixedContextSource(HotReloadWarmUpTestDoubles.CreateReadyCapture()), items);
        }
    }
}
