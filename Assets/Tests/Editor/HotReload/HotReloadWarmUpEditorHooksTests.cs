using System;
using System.Collections.Generic;
using System.Threading.Tasks;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The domain-reload and compile-start callbacks stop the installed warm-up and name
    /// themselves as what stopped it.
    /// </summary>
    public sealed class HotReloadWarmUpEditorHooksTests
    {
        private Func<HotReloadServices> _originalGetServices;
        private List<string> _ran;
        private PendingWarmUpItem _pending;
        private HotReloadWarmUp _warmUp;
        private HotReloadDomainTestScope _scope;

        [SetUp]
        public void SetUp()
        {
            _originalGetServices = HotReloadWarmUpEditorHooks.GetServices;
            _ran = new List<string>();
            _pending = new PendingWarmUpItem("a", _ran);
            _warmUp = new HotReloadWarmUp(
                new FixedContextSource(HotReloadWarmUpTestDoubles.CreateReadyCapture()),
                new IHotReloadWarmUpItem[] { _pending, new RecordingWarmUpItem("b", _ran) });
            _scope = HotReloadDomainTestScope.WithWarmUp(_warmUp);
            HotReloadWarmUpEditorHooks.GetServices = () => HotReloadCompositionRoot.Services;
            VibeLogger.ClearMemoryLogs();
        }

        [TearDown]
        public void TearDown()
        {
            _pending.Release.TrySetResult(true);
            _scope.Dispose();
            HotReloadWarmUpEditorHooks.GetServices = _originalGetServices;
            VibeLogger.ClearMemoryLogs();
        }

        /// <summary>
        /// What: the reload callback stops a running warm-up and records beforeAssemblyReload.
        /// </summary>
        [Test]
        public async Task ShutdownForReload_StopsTheRunningWarmUp()
        {
            await StopWith(HotReloadWarmUpEditorHooks.ShutdownForReload);

            AssertStoppedBy(HotReloadConstants.WarmUpShutdownTriggerBeforeAssemblyReload);
        }

        /// <summary>
        /// What: the compile-start callback stops a running warm-up and records compilationStarted.
        /// </summary>
        [Test]
        public async Task ShutdownForCompile_StopsTheRunningWarmUp()
        {
            await StopWith(() => HotReloadWarmUpEditorHooks.ShutdownForCompile(null));

            AssertStoppedBy(HotReloadConstants.WarmUpShutdownTriggerCompilationStarted);
        }

        private async Task StopWith(Action hook)
        {
            _warmUp.Start();
            Assert.That(_warmUp.State, Is.EqualTo(HotReloadWarmUpState.Running), "Precondition: item a must be in flight.");

            hook();
            _pending.Release.TrySetResult(true);
            await _warmUp.Completion;
        }

        private void AssertStoppedBy(string trigger)
        {
            Assert.That(_ran, Is.EqualTo(new[] { "a" }), "items run");
            Assert.That(HotReloadWarmUpTestDoubles.ReadCompletedOutcomes(), Is.EqualTo(new[] { "a:done", "b:cancelled" }));
            Assert.That(
                (string)HotReloadWarmUpTestDoubles.ReadSingleVibeContext(HotReloadConstants.VibeLogWarmUpComplete)["cancelledBy"],
                Is.EqualTo(trigger));
        }
    }
}
