using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Replaces production AssetDatabase hold actions for HotReload EditMode tests.
    /// Why: RunAsync and the 0.5s reconcile would otherwise Refresh during tests.
    /// </summary>
    [SetUpFixture]
    public sealed class HotReloadAutoRefreshHoldTestSafety
    {
        private static bool _held;

        [OneTimeSetUp]
        public void InstallNoOpHoldActions()
        {
            _held = false;
            HotReloadAutoRefreshHold.OverrideServiceForTesting = new HotReloadAutoRefreshHoldService(
                () => _held,
                value => _held = value,
                () => true,
                () => false,
                () => { },
                () => { },
                () => { });
        }

        [OneTimeTearDown]
        public void RestoreProductionHoldActions()
        {
            HotReloadAutoRefreshHold.OverrideServiceForTesting = null;
        }
    }
}
