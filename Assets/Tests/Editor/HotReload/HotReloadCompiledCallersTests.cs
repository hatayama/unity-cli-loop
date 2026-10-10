using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Contract of the compiled-caller analysis at its entry point: when a run takes the call-site
    /// cache hold relative to a background read, and when ending or disposing the run releases it
    /// and starts the background reads. Each test uses a cache and a loader of its own and touches
    /// neither the installed services nor the shared cache.
    /// </summary>
    public sealed class HotReloadCompiledCallersTests
    {
        private PendingDllLoader _loader;
        private HotReloadCompiledCallSiteCache _cache;
        private HotReloadCallSiteBackfill _backfill;
        private HotReloadCompiledCallers _compiledCallers;

        [SetUp]
        public void SetUp()
        {
            _loader = new PendingDllLoader();
            _cache = new HotReloadCompiledCallSiteCache(HotReloadCompiledCallSiteCache.DefaultBudgetBytes);
            _backfill = new HotReloadCallSiteBackfill(_loader.Load);
            _compiledCallers = new HotReloadCompiledCallers(_cache, _backfill);
            VibeLogger.ClearMemoryLogs();
        }

        [TearDown]
        public void TearDown()
        {
            _loader.ReleaseAll();
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: a run begins only after the background read in flight ended, so the hold is not
        /// taken while the read holds the cache, and the backfill stops before its next dll.
        /// </summary>
        [Test]
        public async Task BeginRunAsync_WhileABackgroundReadIsInFlight_TakesTheHoldOnlyAfterTheReadEnds()
        {
            _loader.PendingFor.Add("a.dll");
            _backfill.Start(new[] { "a.dll", "b.dll" }, "corr");

            Task<HotReloadCompiledCallersRun> begin = _compiledCallers.BeginRunAsync();
            Assert.That(begin.IsCompleted, Is.False, "the run waits for the read in flight");
            Assert.That(_cache.HoldDepth, Is.EqualTo(0), "no hold while the read is in flight");

            _loader.Release("a.dll");
            using (HotReloadCompiledCallersRun run = await begin)
            {
                Assert.That(_cache.HoldDepth, Is.EqualTo(1), "the run holds the entries");
                Assert.That(_loader.Ran, Is.EqualTo(new[] { "a.dll" }), "paths read");
            }

            Assert.That(_cache.HoldDepth, Is.EqualTo(0), "disposing the run releases the hold");
        }

        /// <summary>
        /// What: ending a run releases its hold before the background read of the dlls its notes
        /// could not read starts, so those reads evict as reads outside a run do.
        /// </summary>
        [Test]
        public async Task End_ReleasesTheHoldBeforeTheBackgroundReadOfTheRefusedDllsStarts()
        {
            List<int> holdDepthAtLoad = new List<int>();
            HotReloadCallSiteBackfill backfill = new HotReloadCallSiteBackfill((dllPath, ct) =>
            {
                holdDepthAtLoad.Add(_cache.HoldDepth);
                return Task.CompletedTask;
            });
            HotReloadCompiledCallers compiledCallers = new HotReloadCompiledCallers(_cache, backfill);
            HotReloadCompiledCallersRun run = await compiledCallers.BeginRunAsync();
            run.RememberRefusedDllPathsForTesting(new[] { "refused.dll" });

            run.End("corr");
            await backfill.Completion;
            run.Dispose();

            Assert.That(holdDepthAtLoad, Is.EqualTo(new[] { 0 }), "the refused dll is read once, with no hold open");
            Assert.That(_cache.HoldDepth, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a run that ends without End (an exception) releases its hold and reads none of the
        /// dlls its notes could not read.
        /// </summary>
        [Test]
        public async Task Dispose_WithoutEnd_ReleasesTheHoldAndStartsNoBackgroundRead()
        {
            HotReloadCompiledCallersRun run = await _compiledCallers.BeginRunAsync();
            run.RememberRefusedDllPathsForTesting(new[] { "refused.dll" });

            run.Dispose();

            Assert.That(_cache.HoldDepth, Is.EqualTo(0), "the hold is released");
            Assert.That(_loader.Ran, Is.Empty, "no background read starts");
            Assert.That(_backfill.State, Is.EqualTo(HotReloadCallSiteBackfillState.Idle));
        }
    }
}
