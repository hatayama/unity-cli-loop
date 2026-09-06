using NUnit.Framework;

using UnityEditor;

using io.github.hatayama.UnityCliLoop.InternalAPIBridge;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Keeps the Editor loop ticking while the HotReload EditMode fixtures run.
    /// Why: the fixtures are async and each awaited continuation resumes on the next
    /// EditorApplication.update, which fires only every ~100 ms in an unfocused Editor; the
    /// pump drives it at <see cref="PumpIntervalMilliseconds"/> for the duration of the run only,
    /// so normal Editor behavior outside the test run is unchanged.
    /// </summary>
    [SetUpFixture]
    public sealed class HotReloadEditorTickPumpTestSafety
    {
        private const int PumpIntervalMilliseconds = 8;

        private static EditorTickPump _pump;

        [OneTimeSetUp]
        public void StartPump()
        {
            StopPump();
            // EditorApplication.SignalTick is [ThreadSafe] in the Editor bindings, which is what
            // allows the timer thread to call it.
            _pump = new EditorTickPump(EditorApplicationTickBridge.SignalTick, PumpIntervalMilliseconds);
            // Why: a compile or a test-triggered reload tears the domain down while the run is
            // still open; the timer must stop before that so it cannot signal across the reload.
            AssemblyReloadEvents.beforeAssemblyReload -= StopPump;
            AssemblyReloadEvents.beforeAssemblyReload += StopPump;
        }

        [OneTimeTearDown]
        public void StopPumpAfterRun()
        {
            AssemblyReloadEvents.beforeAssemblyReload -= StopPump;
            StopPump();
        }

        private static void StopPump()
        {
            EditorTickPump pump = _pump;
            _pump = null;
            pump?.Dispose();
        }
    }
}
