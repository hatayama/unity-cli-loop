using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage for the caller-note backfill around a hot reload run: a run
    /// whose note step refused a dll reads it in the background afterwards, so the next run reads
    /// nothing, and a run that comes while a backfill reads waits for that dll only.
    /// </summary>
    public class HotReloadCallerNoteBackfillE2ETests
    {
        private const string BackfillFixtureFileName = "HotReloadCallerNoteBackfillE2EFixture.cs";
        private const string CacheFixtureFileName = "HotReloadCallSiteCacheE2EFixture.cs";
        private const string CrossAssemblyName = "UnityCLILoop.Tests.Editor.HotReload.CallSiteCrossAssembly";
        private const string ReadBody = "return 1;";
        private const int HeldReadMilliseconds = 100;

        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            // Why clear here too: an earlier test may leave a load-budget entry behind, and this
            // class counts those entries.
            VibeLogger.ClearMemoryLogs();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _scope?.Dispose();
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a cold run whose note step refused the caller's dll reads that dll in the
        /// background after the run, so the next run on the same file reads no dll and refuses none.
        /// </summary>
        [Test]
        public async Task Run_WhoseNoteStepRefusedADll_BackfillsItSoTheNextRunReadsNothing()
        {
            string fixturePath = FixturePath(BackfillFixtureFileName);
            string source = File.ReadAllText(fixturePath);
            Assert.That(source, Does.Contain(ReadBody), "Precondition: the Read body anchor must exist.");
            _scope = new HotReloadDomainTestScope();
            await _scope.InstalledWarmUpStopped;
            // Why clear: entries cached by earlier tests would let the first run refuse nothing,
            // which would leave the backfill nothing to show.
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;

            HotReloadOrchestratorResult first = await RunWithReadReturningAsync(
                fixturePath,
                source,
                2,
                () => new HotReloadCallerNoteBackfillE2EFixture().Read());
            Assert.That(
                HotReloadCompiledCallSiteCache.Shared.LoadCount - before,
                Is.EqualTo(1),
                "The first run reads the fixture's own assembly and the budget refuses the caller's.\n" + FormatOutcomes(first));
            JArray exhausted = JArray.Parse(VibeLogger.GetLogsForAi(HotReloadConstants.VibeLogCallerNoteLoadBudgetExhausted));
            Assert.That(exhausted.Count, Is.EqualTo(1), "Precondition: the first run's budget must refuse a dll.");
            Assert.That(
                exhausted[0]["context"]["refusedAssemblies"].ToObject<string[]>(),
                Is.EqualTo(new[] { CrossAssemblyName }),
                "Precondition: the refused dll must be the cross-assembly caller.");

            await HotReloadCompositionRoot.Services.CompiledCallers.Backfill.Completion;
            Assert.That(
                HotReloadCompiledCallSiteCache.Shared.LoadCount - before,
                Is.EqualTo(2),
                "The backfill reads the refused dll.");
            JObject backfill = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogCallerNoteBackfillComplete);
            Assert.That((int)backfill["requested"], Is.EqualTo(1), "requested");
            Assert.That((int)backfill["loaded"], Is.EqualTo(1), "loaded");
            Assert.That((int)backfill["failed"], Is.EqualTo(0), "failed");
            Assert.That(backfill["cancelledBy"].Type, Is.EqualTo(JTokenType.Null), "cancelledBy");

            HotReloadOrchestratorResult second = await RunWithReadReturningAsync(
                fixturePath,
                source,
                3,
                () => new HotReloadCallerNoteBackfillE2EFixture().Read());
            Assert.That(
                HotReloadCompiledCallSiteCache.Shared.LoadCount - before,
                Is.EqualTo(2),
                "The second run reads no dll.\n" + FormatOutcomes(second));
            Assert.That(
                JArray.Parse(VibeLogger.GetLogsForAi(HotReloadConstants.VibeLogCallerNoteLoadBudgetExhausted)).Count,
                Is.EqualTo(1),
                "The second run's budget refuses nothing.");
        }

        /// <summary>
        /// What: a run that comes while the previous run's backfill is reading a dll waits for
        /// that dll, applies, and the backfill reads no later dll; the entry names the run.
        /// </summary>
        [Test]
        public async Task Run_WhileTheBackfillOfThePreviousRunIsInFlight_WaitsForThatDllAndStopsTheRest()
        {
            string fixturePath = FixturePath(CacheFixtureFileName);
            string source = File.ReadAllText(fixturePath);
            Assert.That(source, Does.Contain(ReadBody), "Precondition: the Read body anchor must exist.");
            PendingDllLoader loader = new PendingDllLoader();
            loader.PendingFor.Add("a.dll");
            HotReloadCallSiteBackfill backfill = new HotReloadCallSiteBackfill(loader.Load);
            _scope = HotReloadDomainTestScope.WithCallSiteBackfill(backfill);
            await _scope.InstalledWarmUpStopped;
            backfill.Start(new[] { "a.dll", "b.dll" }, "corr");

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "CallerNoteBackfillE2E.cs",
                source.Replace(ReadBody, "return 2;"));
            Task<HotReloadOrchestratorResult> run = HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
            Assert.That(run.IsCompleted, Is.False, "Precondition: the run must be waiting on its first await.");
            Assert.That(loader.Ran, Is.EqualTo(new[] { "a.dll" }), "paths read while the run waits");
            loader.Release("a.dll");
            HotReloadOrchestratorResult result = await AwaitRunAsync(run);

            Assert.That(
                new HotReloadCallSiteCacheE2EFixture().Read(),
                Is.EqualTo(2),
                "The run applies after the dll in flight.\n" + FormatOutcomes(result));
            Assert.That(loader.Ran, Is.EqualTo(new[] { "a.dll" }), "the backfill reads no later dll");
            JObject context = HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogCallerNoteBackfillComplete);
            Assert.That((int)context["loaded"], Is.EqualTo(1), "loaded");
            Assert.That((string)context["cancelledBy"], Is.EqualTo(HotReloadConstants.WarmUpCancelledByRun), "cancelledBy");
        }

        /// <summary>
        /// What: a run that comes while a backfill read is in flight counts the wait for that read
        /// in its warm_up_yield step, so a slow start shows where its time went.
        /// </summary>
        [Test]
        public async Task Run_WhileABackfillReadIsInFlight_CountsTheWaitInWarmUpYield()
        {
            string fixturePath = FixturePath(CacheFixtureFileName);
            string source = File.ReadAllText(fixturePath);
            Assert.That(source, Does.Contain(ReadBody), "Precondition: the Read body anchor must exist.");
            PendingDllLoader loader = new PendingDllLoader();
            loader.PendingFor.Add("a.dll");
            HotReloadCallSiteBackfill backfill = new HotReloadCallSiteBackfill(loader.Load);
            _scope = HotReloadDomainTestScope.WithCallSiteBackfill(backfill);
            await _scope.InstalledWarmUpStopped;
            VibeLogger.ClearMemoryLogs();
            backfill.Start(new[] { "a.dll" }, "corr");

            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "CallerNoteBackfillE2E.cs",
                source.Replace(ReadBody, "return 4;"));
            Task<HotReloadOrchestratorResult> run = HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                new[] { fixturePath },
                editedPath,
                CancellationToken.None);
            Assert.That(run.IsCompleted, Is.False, "Precondition: the run must be waiting for the read.");
            // Why measure here: the run's warm_up_yield step started before RunAsync returned and
            // ends only after the release below, so it covers at least this span.
            Stopwatch held = Stopwatch.StartNew();
            await Task.Delay(HeldReadMilliseconds);
            long heldMs = held.ElapsedMilliseconds;
            loader.Release("a.dll");
            HotReloadOrchestratorResult result = await AwaitRunAsync(run);

            JArray details = JArray.Parse(VibeLogger.GetLogsForAi(HotReloadConstants.VibeLogTimingDetail));
            Assert.That(details.Count, Is.EqualTo(1), "timing details\n" + FormatOutcomes(result));
            long warmUpYieldMs = -1;
            foreach (JToken step in (JArray)details[0]["context"]["steps"])
            {
                if ((string)step["step"] == HotReloadConstants.TimingDetailStepWarmUpYield)
                {
                    warmUpYieldMs = (long)step["ms"];
                }
            }

            Assert.That(warmUpYieldMs, Is.GreaterThanOrEqualTo(heldMs), "the wait for the read counts in warm_up_yield");
        }

        private static async Task<HotReloadOrchestratorResult> RunWithReadReturningAsync(
            string fixturePath,
            string source,
            int value,
            Func<int> readApplied)
        {
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "CallerNoteBackfillE2E.cs",
                source.Replace(ReadBody, "return " + value + ";"));
            HotReloadOrchestratorResult result = await AwaitRunAsync(
                HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                    new[] { fixturePath },
                    editedPath,
                    CancellationToken.None));
            Assert.That(
                readApplied(),
                Is.EqualTo(value),
                "Precondition: the run must patch Read, so its caller scan runs.\n" + FormatOutcomes(result));
            return result;
        }

        private static async Task<HotReloadOrchestratorResult> AwaitRunAsync(Task<HotReloadOrchestratorResult> run)
        {
            HotReloadOrchestratorResult result = null;
            try
            {
                result = await run;
            }
            catch (OperationCanceledException exception)
            {
                // Why fail here: the test framework records an async test that ends canceled as
                // passed, which would hide a run that never finished.
                Assert.Fail("The run was canceled: " + exception.Message);
            }

            return result;
        }

        private static string FixturePath(string fileName)
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", fileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }

        private static string FormatOutcomes(HotReloadOrchestratorResult result)
        {
            List<string> lines = new List<string>();
            foreach (HotReloadMethodOutcome outcome in result.Methods)
            {
                lines.Add(outcome.Kind + " " + outcome.Method + " @" + outcome.FilePath + " :: " + outcome.Reason);
            }

            lines.AddRange(result.Warnings ?? new List<string>());
            return string.Join("\n", lines);
        }
    }
}
