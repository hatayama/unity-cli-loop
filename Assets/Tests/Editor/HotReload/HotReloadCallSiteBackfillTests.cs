using System;
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
    /// Contract of the caller-note backfill: it reads the refused dlls in order after a run, a run
    /// or a shutdown stops it after the dll in flight, and it reports one entry. The loader is a
    /// stand-in; a path held by a TaskCompletionSource stands for a dll being read.
    /// </summary>
    public sealed class HotReloadCallSiteBackfillTests
    {
        private PendingDllLoader _loader;
        private HotReloadCallSiteBackfill _backfill;

        [SetUp]
        public void SetUp()
        {
            _loader = new PendingDllLoader();
            _backfill = new HotReloadCallSiteBackfill(_loader.Load);
            VibeLogger.ClearMemoryLogs();
        }

        [TearDown]
        public void TearDown()
        {
            _loader.ReleaseAll();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a started backfill reads every path in order, returns to idle, and reports the
        /// counts in one complete entry with no cancel reason.
        /// </summary>
        [Test]
        public async Task Start_LoadsEachPathInOrder_AndLogsOneCompleteEntry()
        {
            _backfill.Start(new[] { "a.dll", "b.dll" }, "corr");
            await _backfill.Completion;

            Assert.That(_loader.Ran, Is.EqualTo(new[] { "a.dll", "b.dll" }), "paths read");
            Assert.That(_backfill.State, Is.EqualTo(HotReloadCallSiteBackfillState.Idle), "state");
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogCallerNoteBackfillComplete);
            Assert.That((int)context["requested"], Is.EqualTo(2), "requested");
            Assert.That((int)context["loaded"], Is.EqualTo(2), "loaded");
            Assert.That((int)context["failed"], Is.EqualTo(0), "failed");
            Assert.That(context["cancelledBy"].Type, Is.EqualTo(JTokenType.Null), "cancelledBy");
        }

        /// <summary>
        /// What: an empty list starts nothing and leaves no entry behind.
        /// </summary>
        [Test]
        public void Start_WithNoPaths_LoadsNothingAndLogsNothing()
        {
            _backfill.Start(Array.Empty<string>(), "corr");

            Assert.That(_backfill.Completion.IsCompleted, Is.True, "completion");
            Assert.That(_loader.Ran, Is.Empty, "paths read");
            Assert.That(CountCompleteEntries(), Is.EqualTo(0), "complete entries");
        }

        /// <summary>
        /// What: a run that yields while a dll is being read waits for that dll, and no later dll
        /// is read; the entry names the run as what stopped the backfill.
        /// </summary>
        [Test]
        public async Task YieldToRunAsync_WhileADllIsInFlight_WaitsForItAndLoadsNoLaterOne()
        {
            _loader.PendingFor.Add("a.dll");
            _backfill.Start(new[] { "a.dll", "b.dll" }, "corr");

            Task yielded = _backfill.YieldToRunAsync();
            Assert.That(yielded.IsCompleted, Is.False, "the yield must wait while a.dll is being read");
            _loader.Release("a.dll");
            await yielded;

            Assert.That(_loader.Ran, Is.EqualTo(new[] { "a.dll" }), "paths read");
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogCallerNoteBackfillComplete);
            Assert.That((int)context["loaded"], Is.EqualTo(1), "loaded");
            Assert.That((string)context["cancelledBy"], Is.EqualTo(HotReloadConstants.WarmUpCancelledByRun), "cancelledBy");
        }

        /// <summary>
        /// What: a run that yields while no backfill runs is not held up and no entry is written.
        /// </summary>
        [Test]
        public void YieldToRunAsync_WhenIdle_CompletesAtOnce()
        {
            Assert.That(_backfill.YieldToRunAsync().IsCompleted, Is.True, "yield");
            Assert.That(CountCompleteEntries(), Is.EqualTo(0), "complete entries");
        }

        /// <summary>
        /// What: a shutdown while no backfill runs is not held up and no entry is written.
        /// </summary>
        [Test]
        public void Shutdown_WhenIdle_CompletesAtOnceAndLogsNothing()
        {
            Assert.That(
                _backfill.Shutdown(HotReloadConstants.WarmUpShutdownTriggerCompilationStarted).IsCompleted,
                Is.True,
                "shutdown");
            Assert.That(CountCompleteEntries(), Is.EqualTo(0), "complete entries");
        }

        /// <summary>
        /// What: when the dll in flight fails after a run yielded, the yield still completes, the
        /// failure is counted, and the run is named as what stopped the backfill.
        /// </summary>
        [Test]
        public async Task YieldToRunAsync_WhileTheInFlightDllFails_CompletesAndCountsThatDllFailed()
        {
            _loader.PendingFor.Add("a.dll");
            LogAssert.Expect(LogType.Warning, new Regex("caller-note backfill"));
            _backfill.Start(new[] { "a.dll", "b.dll" }, "corr");

            Task yielded = _backfill.YieldToRunAsync();
            _loader.Fail("a.dll", new IOException("test: cannot read a.dll"));
            await yielded;

            Assert.That(_loader.Ran, Is.EqualTo(new[] { "a.dll" }), "paths read");
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogCallerNoteBackfillComplete);
            Assert.That((int)context["loaded"], Is.EqualTo(0), "loaded");
            Assert.That((int)context["failed"], Is.EqualTo(1), "failed");
            Assert.That((string)context["cancelledBy"], Is.EqualTo(HotReloadConstants.WarmUpCancelledByRun), "cancelledBy");
        }

        /// <summary>
        /// What: a shutdown while a dll is being read waits for that dll, reads no later one, and
        /// names its trigger in the entry.
        /// </summary>
        [Test]
        public async Task Shutdown_WhileADllIsInFlight_StopsBeforeTheNextOneAndRecordsTheTrigger()
        {
            _loader.PendingFor.Add("a.dll");
            _backfill.Start(new[] { "a.dll", "b.dll" }, "corr");

            Task stopped = _backfill.Shutdown(HotReloadConstants.WarmUpShutdownTriggerCompilationStarted);
            _loader.Release("a.dll");
            await stopped;

            Assert.That(_loader.Ran, Is.EqualTo(new[] { "a.dll" }), "paths read");
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogCallerNoteBackfillComplete);
            Assert.That((string)context["cancelledBy"], Is.EqualTo("compilationStarted"), "cancelledBy");
        }

        /// <summary>
        /// What: a dll that cannot be read is counted as failed with a warning, and the next dll
        /// is still read.
        /// </summary>
        [Test]
        public async Task Start_WhenALoadThrowsAnIoException_CountsItFailedAndLoadsTheNextOne()
        {
            _loader.ThrowFor.Add("a.dll");
            LogAssert.Expect(LogType.Warning, new Regex("caller-note backfill"));

            _backfill.Start(new[] { "a.dll", "b.dll" }, "corr");
            await _backfill.Completion;

            Assert.That(_loader.Ran, Is.EqualTo(new[] { "a.dll", "b.dll" }), "paths read");
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogCallerNoteBackfillComplete);
            Assert.That((int)context["loaded"], Is.EqualTo(1), "loaded");
            Assert.That((int)context["failed"], Is.EqualTo(1), "failed");
        }

        /// <summary>
        /// What: an unexpected exception ends the backfill, is logged, and leaves the completion
        /// task unfaulted, the backfill idle, and no complete entry behind.
        /// </summary>
        [Test]
        public async Task Start_WhenALoadThrowsAnUnexpectedException_LogsItAndCompletionDoesNotFault()
        {
            _loader.ThrowUnexpectedFor.Add("a.dll");
            LogAssert.Expect(LogType.Exception, new Regex("InvalidOperationException: unexpected"));

            _backfill.Start(new[] { "a.dll", "b.dll" }, "corr");
            await _backfill.Completion;

            Assert.That(_backfill.Completion.IsFaulted, Is.False, "completion faulted");
            Assert.That(_backfill.State, Is.EqualTo(HotReloadCallSiteBackfillState.Idle), "state");
            Assert.That(CountCompleteEntries(), Is.EqualTo(0), "complete entries");
        }

        private static int CountCompleteEntries()
        {
            return JArray.Parse(VibeLogger.GetLogsForAi(HotReloadConstants.VibeLogCallerNoteBackfillComplete)).Count;
        }
    }
}
