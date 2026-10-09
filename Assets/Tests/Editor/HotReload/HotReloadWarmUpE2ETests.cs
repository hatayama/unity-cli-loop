using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json.Linq;

using NUnit.Framework;

using UnityEditor.Compilation;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// End-to-end EditMode coverage of the warm-up and a real hot reload run: the run reuses what
    /// the warm-up loaded, a run before the warm-up keeps it from starting, a run during it waits
    /// for the item in flight, and every run records its assembly for the next domain's warm-up.
    /// </summary>
    public class HotReloadWarmUpE2ETests
    {
        private const string FixtureFileName = "HotReloadCallSiteCacheE2EFixture.cs";
        private const string FixtureProjectRelativePath = "Assets/Tests/Editor/HotReload/" + FixtureFileName;
        private const string ReadBody = "return 1;";
        private const string LedgerSentinel = "WarmUpLedgerSentinel";

        private HotReloadDomainTestScope _scope;
        private List<string> _ran;
        private string _savedLedger;

        [SetUp]
        public void SetUp()
        {
            _ran = new List<string>();
            string ledgerPath = LedgerPath();
            _savedLedger = File.Exists(ledgerPath) ? File.ReadAllText(ledgerPath) : null;
            VibeLogger.ClearMemoryLogs();
        }

        [TearDown]
        public void TearDown()
        {
            _scope?.Dispose();
            _scope = null;
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            RestoreLedger();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: after the real warm-up finished, the run reads no compiled assembly the warm-up
        /// already loaded for the edited assembly.
        /// </summary>
        [Test]
        public async Task Run_AfterTheWarmUpFinished_ReadsNoCompiledAssemblyForTheEditedAssembly()
        {
            string testDll = TestAssemblyDllPath();
            HotReloadWarmUp warmUp = new HotReloadWarmUp(
                new FixedContextSource(CreateTestAssemblyCapture(testDll)),
                HotReloadWarmUpItems.CreateProduction());
            await BeginScope(warmUp);
            // Why clear again once the installed warm-up stopped: its unit in flight can log a copy
            // written and its completion after the clear in SetUp.
            VibeLogger.ClearMemoryLogs();
            // Why delete: a copy left by an earlier test would make the run hit the cache even
            // when the warm-up wrote nothing.
            PublicizedCopyTestCache.DeleteCopiesOf(TestAssemblyName(), HotReloadConstants.PublicizedRefsRelativeDirectory);
            // Why clear and require a read: the run loads the edited assembly's dll itself, and
            // the entry outlives the run's hold, so "the run read nothing" alone would pass even
            // when the warm-up loaded nothing.
            HotReloadCompiledCallSiteCache.Shared.Clear();
            int before = HotReloadCompiledCallSiteCache.Shared.LoadCount;

            warmUp.Start();
            await warmUp.Completion;
            int afterWarmUp = HotReloadCompiledCallSiteCache.Shared.LoadCount;
            HotReloadCompiledCallSiteCache.Entry entry = HotReloadCompiledCallSiteCache.Shared.GetOrLoad(testDll);

            Assert.That(afterWarmUp - before, Is.GreaterThanOrEqualTo(1), "reads by the warm-up");
            Assert.That(HotReloadCompiledCallSiteCache.Shared.LoadCount, Is.EqualTo(afterWarmUp), "the warm-up loaded the test dll");
            Assert.That(
                HotReloadWarmUpTestDoubles.ReadCompletedOutcomes(),
                Is.EqualTo(new[] { "publicized_targets:done", "call_sites:done", "referenced_method_sets:done", "pdb_documents:done" }));
            Assert.That(CountTargetCopiesWritten(), Is.EqualTo(1), "copies the warm-up wrote for the edited assembly");
            VibeLogger.ClearMemoryLogs();

            await RunWithReadReturningAsync(2);

            Assert.That(CountTargetCopiesWritten(), Is.EqualTo(0), "copies the run wrote again for the edited assembly");

            Assert.That(HotReloadCompiledCallSiteCache.Shared.GetOrLoad(testDll), Is.SameAs(entry), "the run read the dll again");
        }

        /// <summary>
        /// What: a run that comes before the warm-up's tick keeps the warm-up from starting.
        /// </summary>
        [Test]
        public async Task Run_ThatStartedBeforeTheTick_LeavesTheWarmUpUnstarted()
        {
            HotReloadWarmUp warmUp = CreateWarmUp(new RecordingWarmUpItem("a", _ran));
            await BeginScope(warmUp);

            await RunWithReadReturningAsync(3);
            warmUp.Start();

            Assert.That(_ran, Is.Empty, "items run");
            Assert.That(
                (string)HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogWarmUpSkipped)["reason"],
                Is.EqualTo("run_started_first"));
        }

        /// <summary>
        /// What: a run that comes while an item is in flight does not start before that item
        /// finishes, then applies and reports the wait as the warm_up_yield step.
        /// </summary>
        [Test]
        public async Task Run_WhileAnItemIsInFlight_WaitsForItAndStartsOnlyAfterward()
        {
            PendingWarmUpItem pending = new PendingWarmUpItem("a", _ran);
            HotReloadWarmUp warmUp = CreateWarmUp(pending);
            await BeginScope(warmUp);
            VibeLogger.ClearMemoryLogs();
            Task<HotReloadOrchestratorResult> run;
            try
            {
                warmUp.Start();
                run = RunWithReadReturningAsync(4);

                // Why this shows the wait: without it the run passes its first main-thread switch
                // synchronously here and writes its file start before the task comes back.
                Assert.That(ReadEntries(HotReloadConstants.VibeLogFileStart), Is.Empty, "file start before the item finished");
            }
            finally
            {
                pending.Release.TrySetResult(true);
            }

            await run;

            Assert.That(ReadEntries(HotReloadConstants.VibeLogFileStart), Is.Not.Empty, "file start after the item finished");
            JArray timingDetails = ReadEntries(HotReloadConstants.VibeLogTimingDetail);
            Assert.That(timingDetails.Count, Is.EqualTo(1), "timing details");
            List<string> steps = new List<string>();
            foreach (JToken step in (JArray)timingDetails[0]["context"]["steps"])
            {
                steps.Add((string)step["step"]);
            }

            Assert.That(steps, Does.Contain(HotReloadConstants.TimingDetailStepWarmUpYield));
        }

        /// <summary>
        /// What: a run puts the edited assembly first in the project's ledger and keeps the names
        /// recorded before it.
        /// </summary>
        [Test]
        public async Task Run_RecordsTheEditedAssemblyInTheLedger()
        {
            await BeginScope(HotReloadWarmUpTestDoubles.CreateInert());
            string root = ProjectRoot();
            // Why a sentinel first: every run in this project records the test assembly, so a
            // ledger that already starts with it could not show this run's record.
            HotReloadWarmUpTargetLedger.Record(root, new[] { LedgerSentinel });
            Assert.That(HotReloadWarmUpTargetLedger.Read(root)[0], Is.EqualTo(LedgerSentinel), "Precondition: the sentinel must lead.");

            await RunWithReadReturningAsync(5);

            IReadOnlyList<string> names = HotReloadWarmUpTargetLedger.Read(root);
            Assert.That(names[0], Is.EqualTo(TestAssemblyName()), "first");
            Assert.That(names[1], Is.EqualTo(LedgerSentinel), "second");
        }

        private async Task BeginScope(HotReloadWarmUp warmUp)
        {
            _scope = HotReloadDomainTestScope.WithWarmUp(warmUp);
            HotReloadAutoRefreshHold.SyncToActiveChanges();
            await _scope.InstalledWarmUpStopped;
        }

        private HotReloadWarmUp CreateWarmUp(params IHotReloadWarmUpItem[] items)
        {
            return new HotReloadWarmUp(new FixedContextSource(HotReloadWarmUpTestDoubles.CreateReadyCapture()), items);
        }

        private static HotReloadWarmUpCapture CreateTestAssemblyCapture(string testDll)
        {
            string root = ProjectRoot();
            return HotReloadWarmUpCapture.Ready(
                new HotReloadWarmUpContext(
                    root,
                    new[]
                    {
                        new HotReloadWarmUpTarget(
                            TestAssemblyName(),
                            testDll,
                            Path.ChangeExtension(testDll, ".pdb"),
                            HotReloadCallSiteScanner.CollectReferencingDllPaths(root, TestAssemblyName()))
                    }));
        }

        private static async Task<HotReloadOrchestratorResult> RunWithReadReturningAsync(int value)
        {
            string fixturePath = FixturePath();
            string source = File.ReadAllText(fixturePath);
            Assert.That(source, Does.Contain(ReadBody), "Precondition: the Read body anchor must exist.");
            string editedPath = HotReloadTestSourceWriter.WriteEditedSource(
                "WarmUpE2E.cs",
                source.Replace(ReadBody, "return " + value + ";"));
            HotReloadOrchestratorResult result = null;
            try
            {
                result = await HotReloadCompositionRoot.Services.Orchestrator.RunAsync(
                    new[] { fixturePath },
                    editedPath,
                    CancellationToken.None);
            }
            catch (OperationCanceledException exception)
            {
                // Why fail here: the test framework records an async test that ends canceled as
                // passed, which would hide a run that never finished.
                Assert.Fail("The run was canceled: " + exception.Message);
            }

            Assert.That(new HotReloadCallSiteCacheE2EFixture().Read(), Is.EqualTo(value), "Precondition: the run must apply.");
            return result;
        }

        // The publicized copies of the edited assembly written since the logs were last cleared.
        private static int CountTargetCopiesWritten()
        {
            JArray entries = ReadEntries(HotReloadConstants.VibeLogPublicizedCopyWritten);
            int count = 0;
            for (int i = 0; i < entries.Count; i++)
            {
                JToken context = entries[i]["context"];
                if ((string)context["assemblyName"] == TestAssemblyName()
                    && (string)context["variant"] == HotReloadConstants.PublicizedRefsRelativeDirectory)
                {
                    count++;
                }
            }

            return count;
        }

        private static JArray ReadEntries(string operation)
        {
            return JArray.Parse(VibeLogger.GetLogsForAi(operation));
        }

        private void RestoreLedger()
        {
            string ledgerPath = LedgerPath();
            if (_savedLedger != null)
            {
                File.WriteAllText(ledgerPath, _savedLedger);
                return;
            }

            if (File.Exists(ledgerPath))
            {
                File.Delete(ledgerPath);
            }
        }

        private static string LedgerPath()
        {
            return Path.Combine(
                ProjectRoot(),
                HotReloadConstants.WarmUpRelativeDirectory,
                HotReloadConstants.WarmUpTargetsFileName);
        }

        private static string TestAssemblyName()
        {
            return Path.GetFileNameWithoutExtension(
                CompilationPipeline.GetAssemblyNameFromScriptPath(FixtureProjectRelativePath));
        }

        private static string TestAssemblyDllPath()
        {
            return CompiledAssemblyLayout.Resolve(ProjectRoot()).DllPath(TestAssemblyName());
        }

        private static string ProjectRoot()
        {
            return Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        }

        private static string FixturePath()
        {
            string path = Path.GetFullPath(
                Path.Combine(Application.dataPath, "Tests", "Editor", "HotReload", FixtureFileName));
            Assert.That(File.Exists(path), Is.True, "Fixture missing: " + path);
            return path;
        }
    }
}
