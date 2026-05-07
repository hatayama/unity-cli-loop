using NUnit.Framework;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Server Controller Startup Lock behavior.
    /// </summary>
    public class UnityCliLoopServerControllerStartupLockTests
    {
        [Test]
        public void CreateOptionalServerStartingLock_WhenLockCreationSucceeds_ShouldReturnOwnershipToken()
        {
            // Tests that optional startup locks return the ownership token when creation succeeds.
            UnityCliLoopServerControllerService service = CreateControllerService();

            string token = service.CreateOptionalServerStartingLock(() => "token-123");

            Assert.That(token, Is.EqualTo("token-123"));
        }

        [Test]
        public void CreateOptionalServerStartingLock_WhenLockCreationFails_ShouldContinueWithoutThrowing()
        {
            // Tests that optional startup locks do not fail server startup when creation fails.
            UnityCliLoopServerControllerService service = CreateControllerService();

            string token = service.CreateOptionalServerStartingLock(() => null);

            Assert.That(token, Is.Null);
        }

        [Test]
        public void ScheduleStartupRecovery_WhenCalled_ExposesRecoveryTaskBeforeDeferredActionRuns()
        {
            // Tests that deferred startup recovery exposes its pending task before execution.
            System.Action scheduledAction = null;
            bool recoveryExecuted = false;
            UnityCliLoopServerControllerService service = CreateControllerService();

            Task recoveryTask = service.ScheduleStartupRecovery(
                action => scheduledAction = action,
                () =>
                {
                    recoveryExecuted = true;
                    return Task.CompletedTask;
                });

            Assert.That(recoveryExecuted, Is.False);
            Assert.That(scheduledAction, Is.Not.Null);
            Assert.That(recoveryTask, Is.SameAs(service.RecoveryTask));
            Assert.That(recoveryTask.IsCompleted, Is.False);

            scheduledAction();

            Assert.That(recoveryExecuted, Is.True);
            Assert.That(recoveryTask.IsCompleted, Is.True);
            Assert.That(service.RecoveryTask, Is.Null);
        }

        [Test]
        public void ScheduleStartupRecovery_WhenRecoveryThrowsSynchronously_FaultsTaskAndClearsRecoveryTask()
        {
            // Tests that synchronous startup recovery failures fault and clear the tracked task.
            System.Action scheduledAction = null;
            UnityCliLoopServerControllerService service = CreateControllerService();

            Task recoveryTask = service.ScheduleStartupRecovery(
                action => scheduledAction = action,
                () => throw new System.InvalidOperationException("restore failed"));

            scheduledAction();

            Assert.That(recoveryTask.IsFaulted, Is.True);
            Assert.That(service.RecoveryTask, Is.Null);
            Assert.ThrowsAsync<System.InvalidOperationException>(async () => await recoveryTask);
        }

        [Test]
        public async Task ScheduleStartupRecovery_WhenRecoveryIsAsync_KeepsTaskIncompleteUntilRecoveryCompletes()
        {
            // Tests that asynchronous startup recovery remains pending until its restore task completes.
            System.Action scheduledAction = null;
            TaskCompletionSource<bool> recoveryCompletionSource = new();
            UnityCliLoopServerControllerService service = CreateControllerService();

            Task recoveryTask = service.ScheduleStartupRecovery(
                action => scheduledAction = action,
                () => recoveryCompletionSource.Task);

            scheduledAction();

            Assert.That(recoveryTask.IsCompleted, Is.False);
            Assert.That(service.RecoveryTask, Is.SameAs(recoveryTask));

            recoveryCompletionSource.SetResult(true);
            await recoveryTask;

            Assert.That(recoveryTask.IsCompleted, Is.True);
            Assert.That(service.RecoveryTask, Is.Null);
        }

        private static UnityCliLoopServerControllerService CreateControllerService()
        {
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            return new UnityCliLoopServerControllerService(
                serverInstanceFactory,
                lifecycleRegistry);
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstanceFactory : IUnityCliLoopServerInstanceFactory
        {
            public IUnityCliLoopServerInstance Create()
            {
                return new TestServerInstance();
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstance : IUnityCliLoopServerInstance
        {
            public bool IsRunning => false;

            public string Endpoint => "test";

            public void StartServer(bool clearServerStartingLockWhenReady = true)
            {
            }

            public void StopServer()
            {
            }

            public void Dispose()
            {
            }
        }
    }
}
