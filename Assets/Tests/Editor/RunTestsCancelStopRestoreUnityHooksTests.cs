using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies which Unity hooks the run-tests cancel path resolves, without invoking the hooks that stop
    /// Play Mode or wait.
    /// </summary>
    public sealed class RunTestsCancelStopRestoreUnityHooksTests
    {
        /// <summary>
        /// Verifies the production hooks offer cancel and run polling exactly when the bridge resolved them, and
        /// always offer the Play Mode, delay, and warning hooks.
        /// </summary>
        [Test]
        public void CreateDefault_WiresTheCancelHelpersTheBridgeFound()
        {
            RunTestsCancelStopRestoreHooks hooks = RunTestsCancelStopRestoreUnityHooks.CreateDefault();

            Assert.That(hooks.TryCancelTestRun != null, Is.EqualTo(TestRunnerApiCancelBridge.HasCancelTestRun));
            Assert.That(hooks.IsRunActive != null, Is.EqualTo(TestRunnerApiCancelBridge.HasIsRunActive));
            Assert.That(hooks.RequestExitPlayMode, Is.Not.Null);
            Assert.That(hooks.DelayAsync, Is.Not.Null);
        }

        /// <summary>
        /// Verifies the Play Mode hook reads the Editor state and the warning hook writes a Console warning.
        /// </summary>
        [Test]
        public void CreateDefault_ReportsEditModeAndLogsWarningsToTheConsole()
        {
            RunTestsCancelStopRestoreHooks hooks = RunTestsCancelStopRestoreUnityHooks.CreateDefault();
            LogAssert.Expect(LogType.Warning, "stop/restore warning from a test");

            hooks.LogWarning("stop/restore warning from a test");

            Assert.That(hooks.IsPlaying(), Is.False);
        }
    }
}
