using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the server lifecycle contract that application use cases orchestrate.
    /// </summary>
    public class ServerLifecycleContractTests
    {
        [Test]
        // Verifies that the default startup request releases the startup lock after readiness.
        public void ReleaseStartupLockWhenReady_ClearsStartupLock_WhenServerReportsReady()
        {
            ServerInitializationRequest request =
                ServerInitializationRequest.ReleaseStartupLockWhenReady();

            Assert.That(request.StartupLockReleasePolicy, Is.EqualTo(ServerStartupLockReleasePolicy.ReleaseWhenReady));
            Assert.That(request.ClearStartupLockWhenReady, Is.True);
        }

        [Test]
        // Verifies that explicit-release startup requests preserve the startup lock for another owner.
        public void PreserveStartupLockUntilExplicitRelease_KeepsStartupLock_ForExplicitRelease()
        {
            ServerInitializationRequest request =
                ServerInitializationRequest.PreserveStartupLockUntilExplicitRelease();

            Assert.That(
                request.StartupLockReleasePolicy,
                Is.EqualTo(ServerStartupLockReleasePolicy.PreserveUntilExplicitRelease));
            Assert.That(request.ClearStartupLockWhenReady, Is.False);
        }

        [Test]
        // Verifies that a successful initialization result carries the application-owned server handle.
        public void RunningInitializationResult_CarriesServerInstanceAndSuccess()
        {
            object serverInstance = new object();

            ServerInitializationResult<object> result =
                ServerInitializationResult<object>.Running(serverInstance);

            Assert.That(result.Success, Is.True);
            Assert.That(result.IsRunning, Is.True);
            Assert.That(result.Message, Is.EqualTo(ServerLifecycleMessages.InitializationSucceeded));
            Assert.That(result.ServerInstance, Is.SameAs(serverInstance));
        }

        [Test]
        // Verifies that stopping an already stopped server is a successful no-op contract.
        public void AlreadyStoppedShutdownResult_IsSuccessfulWithStableMessage()
        {
            ServerShutdownResult result = ServerShutdownResult.AlreadyStopped();

            Assert.That(result.Success, Is.True);
            Assert.That(result.Message, Is.EqualTo(ServerLifecycleMessages.ShutdownAlreadyStopped));
        }
    }
}
