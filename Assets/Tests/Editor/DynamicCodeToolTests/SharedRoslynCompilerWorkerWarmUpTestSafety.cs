using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Stops the shared-worker warm-up once for the whole run of the tests in this namespace.
    /// Why: a warm-up started by the server reset after the last reload compiles in the shared
    /// worker these tests swap, shut down and count error logs of.
    /// </summary>
    [SetUpFixture]
    public sealed class SharedRoslynCompilerWorkerWarmUpTestSafety
    {
        [OneTimeSetUp]
        public void StopWarmUp()
        {
            // The task is awaited by each fixture's [UnitySetUp]; OneTimeSetUp cannot wait without
            // blocking the main thread.
            _ = DynamicCodeServices.GetRegistry().StopSharedWorkerWarmUpForTests();
        }
    }
}
