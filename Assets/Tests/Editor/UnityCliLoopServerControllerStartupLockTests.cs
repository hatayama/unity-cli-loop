using NUnit.Framework;
using System.Threading;
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

        [Test]
        public async Task StartRecoveryIfNeededAsync_WhenStartupLockExists_ShouldReleaseLockAfterWarmup()
        {
            // Tests that recovery keeps the startup lock until post-bind warmup has completed.
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            UnityCliLoopServerControllerService service = new(
                serverInstanceFactory,
                lifecycleRegistry);
            string claimedLockPath = null;
            ServerStartingLockService.OnOwnedLockFileClaimedForDeletionForTests = path => claimedLockPath = path;

            try
            {
                await service.StartRecoveryIfNeededAsync(isAfterCompile: true, CancellationToken.None);

                Assert.That(serverInstanceFactory.LastCreated.ClearServerStartingLockWhenReady, Is.False);
                Assert.That(claimedLockPath, Is.Not.Null);
            }
            finally
            {
                ServerStartingLockService.OnOwnedLockFileClaimedForDeletionForTests = null;
                ServerStartingLockService.DeleteLockFile();
            }
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
            public TestServerInstance LastCreated { get; private set; }

            public IUnityCliLoopServerInstance Create()
            {
                LastCreated = new TestServerInstance();
                return LastCreated;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstance : IUnityCliLoopServerInstance
        {
            public bool IsRunning { get; private set; }

            public bool? ClearServerStartingLockWhenReady { get; private set; }

            public string Endpoint => "test";

            public void StartServer(bool clearServerStartingLockWhenReady = true)
            {
                ClearServerStartingLockWhenReady = clearServerStartingLockWhenReady;
                IsRunning = true;
            }

            public void StopServer()
            {
                IsRunning = false;
            }

            public void Dispose()
            {
                IsRunning = false;
            }
        }
    }
}
