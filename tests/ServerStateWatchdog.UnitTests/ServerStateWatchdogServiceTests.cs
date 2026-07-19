using System;
using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.UnitTests
{
    [TestFixture]
    public sealed class ServerStateWatchdogServiceTests
    {
        /// <summary>
        /// Verifies that a missing server is recovered when settings claim that it should run.
        /// </summary>
        [Test]
        public void DecideAction_WhenSettingsClaimRunningButServerIsStopped_ShouldRecoverServer()
        {
            WatchdogObservation observation = new(
                settingsClaimServerRunning: true,
                serverIsRunning: false,
                settingsPort: 6000,
                serverPort: null,
                currentUtc: DateTime.UtcNow,
                lastRecoveryAttemptUtc: DateTime.UtcNow.AddSeconds(-31));

            WatchdogAction action = ServerStateWatchdogService.DecideAction(observation);

            Assert.That(action, Is.EqualTo(WatchdogAction.RecoverServer));
        }

        /// <summary>
        /// Verifies that settings follow a running server after a port fallback.
        /// </summary>
        [Test]
        public void DecideAction_WhenServerPortDiffersFromSettings_ShouldRewriteSettings()
        {
            WatchdogObservation observation = new(
                settingsClaimServerRunning: true,
                serverIsRunning: true,
                settingsPort: 6000,
                serverPort: 6001,
                currentUtc: DateTime.UtcNow,
                lastRecoveryAttemptUtc: null);

            WatchdogAction action = ServerStateWatchdogService.DecideAction(observation);

            Assert.That(action, Is.EqualTo(WatchdogAction.RewriteSettings));
        }

        /// <summary>
        /// Verifies that an intentional stop is never undone by the watchdog.
        /// </summary>
        [Test]
        public void DecideAction_WhenSettingsClaimStopped_ShouldDoNothing()
        {
            WatchdogObservation observation = new(
                settingsClaimServerRunning: false,
                serverIsRunning: false,
                settingsPort: 6000,
                serverPort: null,
                currentUtc: DateTime.UtcNow,
                lastRecoveryAttemptUtc: null);

            WatchdogAction action = ServerStateWatchdogService.DecideAction(observation);

            Assert.That(action, Is.EqualTo(WatchdogAction.None));
        }

        /// <summary>
        /// Verifies that repeated recovery attempts are suppressed during the backoff window.
        /// </summary>
        [Test]
        public void DecideAction_WhenRecoveryWasAttemptedRecently_ShouldDoNothing()
        {
            WatchdogObservation observation = new(
                settingsClaimServerRunning: true,
                serverIsRunning: false,
                settingsPort: 6000,
                serverPort: null,
                currentUtc: DateTime.UtcNow,
                lastRecoveryAttemptUtc: DateTime.UtcNow.AddSeconds(-29));

            WatchdogAction action = ServerStateWatchdogService.DecideAction(observation);

            Assert.That(action, Is.EqualTo(WatchdogAction.None));
        }

        /// <summary>
        /// Verifies that startup protection temporarily suppresses recovery.
        /// </summary>
        [Test]
        public void DecideAction_WhenStartupProtectionIsActive_ShouldDoNothing()
        {
            WatchdogObservation observation = new(
                settingsClaimServerRunning: true,
                serverIsRunning: false,
                settingsPort: 6000,
                serverPort: null,
                currentUtc: DateTime.UtcNow,
                lastRecoveryAttemptUtc: null,
                isStartupProtectionActive: true);

            WatchdogAction action = ServerStateWatchdogService.DecideAction(observation);

            Assert.That(action, Is.EqualTo(WatchdogAction.None));
        }

        /// <summary>
        /// Verifies that background Unity processes never start a server.
        /// </summary>
        [Test]
        public void DecideAction_WhenRunningInBackgroundProcess_ShouldDoNothing()
        {
            WatchdogObservation observation = new(
                settingsClaimServerRunning: true,
                serverIsRunning: false,
                settingsPort: 6000,
                serverPort: null,
                currentUtc: DateTime.UtcNow,
                lastRecoveryAttemptUtc: null,
                isBackgroundProcess: true);

            WatchdogAction action = ServerStateWatchdogService.DecideAction(observation);

            Assert.That(action, Is.EqualTo(WatchdogAction.None));
        }
    }
}
