using System;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The domain-reload and compile-start callbacks stop the installed caller-note backfill and
    /// name themselves as what stopped it.
    /// </summary>
    public sealed class HotReloadCallSiteBackfillEditorHooksTests
    {
        private Func<HotReloadServices> _originalGetServices;
        private PendingDllLoader _loader;
        private HotReloadCallSiteBackfill _backfill;
        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _originalGetServices = HotReloadWarmUpEditorHooks.GetServices;
            _loader = new PendingDllLoader();
            _loader.PendingFor.Add("a.dll");
            _backfill = new HotReloadCallSiteBackfill(_loader.Load);
            _scope = HotReloadDomainTestScope.WithCallSiteBackfill(_backfill);
            HotReloadWarmUpEditorHooks.GetServices = () => HotReloadCompositionRoot.Services;
            VibeLogger.ClearMemoryLogs();
        }

        [TearDown]
        public void TearDown()
        {
            _loader.ReleaseAll();
            _scope.Dispose();
            HotReloadWarmUpEditorHooks.GetServices = _originalGetServices;
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: the reload callback stops a running backfill and records beforeAssemblyReload.
        /// </summary>
        [Test]
        public async Task ShutdownForReload_StopsTheRunningBackfill()
        {
            await StopWith(HotReloadWarmUpEditorHooks.ShutdownForReload);

            AssertStoppedBy(HotReloadConstants.WarmUpShutdownTriggerBeforeAssemblyReload);
        }

        /// <summary>
        /// What: the compile-start callback stops a running backfill and records compilationStarted.
        /// </summary>
        [Test]
        public async Task ShutdownForCompile_StopsTheRunningBackfill()
        {
            await StopWith(() => HotReloadWarmUpEditorHooks.ShutdownForCompile(null));

            AssertStoppedBy(HotReloadConstants.WarmUpShutdownTriggerCompilationStarted);
        }

        private async Task StopWith(Action hook)
        {
            _backfill.Start(new[] { "a.dll", "b.dll" }, "corr");
            Assert.That(
                _backfill.State,
                Is.EqualTo(HotReloadCallSiteBackfillState.Running),
                "Precondition: a.dll must be in flight.");

            hook();
            _loader.Release("a.dll");
            await _backfill.Completion;
        }

        private void AssertStoppedBy(string trigger)
        {
            Assert.That(_loader.Ran, Is.EqualTo(new[] { "a.dll" }), "paths read");
            Assert.That(
                (string)HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogCallerNoteBackfillComplete)["cancelledBy"],
                Is.EqualTo(trigger));
        }
    }
}
