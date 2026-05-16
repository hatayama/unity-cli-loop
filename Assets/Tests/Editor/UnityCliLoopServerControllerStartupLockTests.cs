using NUnit.Framework;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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
            UnityCliLoopEditorSettingsService editorSettingsService =
                UnityCliLoopEditorSettingsTestFactory.CreateService();
            ServerReadinessStateStore stateStore = CreateTestStateStore();
            UnityCliLoopServerControllerService service = new(
                serverInstanceFactory,
                lifecycleRegistry,
                new DomainReloadDetectionFileService(editorSettingsService, stateStore),
                editorSettingsService,
                stateStore,
                new TestReadinessProbe());
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

        [Test]
        public async Task StartRecoveryIfNeededAsync_WhenReadinessSucceeds_ShouldPublishReadyState()
        {
            // Tests that recovery writes the ready state only after the readiness probe succeeds.
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            UnityCliLoopEditorSettingsService editorSettingsService =
                UnityCliLoopEditorSettingsTestFactory.CreateService();
            ServerReadinessStateStore stateStore = CreateTestStateStore();
            TestReadinessProbe readinessProbe = new();
            int serverStartedCount = 0;
            lifecycleRegistry.ServerStarted += () => serverStartedCount++;
            UnityCliLoopServerControllerService service = new(
                serverInstanceFactory,
                lifecycleRegistry,
                new DomainReloadDetectionFileService(editorSettingsService, stateStore),
                editorSettingsService,
                stateStore,
                readinessProbe);

            await service.StartRecoveryIfNeededAsync(isAfterCompile: false, CancellationToken.None);

            ServerReadinessState state = stateStore.Read();
            Assert.That(readinessProbe.CallCount, Is.EqualTo(1));
            Assert.That(serverStartedCount, Is.EqualTo(1));
            Assert.That(state.Phase, Is.EqualTo("ready"));
            Assert.That(state.Endpoint, Is.EqualTo("test"));
        }

        [Test]
        public async Task ProbeReadinessWithTimeoutAsync_WhenProbeDoesNotComplete_ThrowsTimeout()
        {
            // Tests that readiness probing fails fast instead of leaving startup state stuck forever.
            TestReadinessProbe readinessProbe = new(neverCompletes: true);
            UnityCliLoopServerControllerService service = CreateControllerService(readinessProbe);

            System.TimeoutException exception = null;
            try
            {
                await service.ProbeReadinessWithTimeoutAsync(CancellationToken.None, 1);
            }
            catch (System.TimeoutException ex)
            {
                exception = ex;
            }

            Assert.That(exception, Is.Not.Null);
            Assert.That(exception.Message, Does.Contain("timed out"));
            Assert.That(readinessProbe.CallCount, Is.EqualTo(1));
        }

        private static UnityCliLoopServerControllerService CreateControllerService()
        {
            return CreateControllerService(new TestReadinessProbe());
        }

        private static UnityCliLoopServerControllerService CreateControllerService(TestReadinessProbe readinessProbe)
        {
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            UnityCliLoopEditorSettingsService editorSettingsService =
                UnityCliLoopEditorSettingsTestFactory.CreateService();
            ServerReadinessStateStore stateStore = CreateTestStateStore();
            return new UnityCliLoopServerControllerService(
                serverInstanceFactory,
                lifecycleRegistry,
                new DomainReloadDetectionFileService(editorSettingsService, stateStore),
                editorSettingsService,
                stateStore,
                readinessProbe);
        }

        private static ServerReadinessStateStore CreateTestStateStore()
        {
            string projectRoot = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "unity-cli-loop-tests",
                System.Guid.NewGuid().ToString("N"));
            return new ServerReadinessStateStore(projectRoot);
        }

        /// <summary>
        /// Test support type that makes readiness probing deterministic and side-effect free.
        /// </summary>
        private sealed class TestReadinessProbe : IUnityCliLoopServerReadinessProbe
        {
            private readonly bool _neverCompletes;

            public TestReadinessProbe(bool neverCompletes = false)
            {
                _neverCompletes = neverCompletes;
            }

            public int CallCount { get; private set; }

            public Task ProbeAsync(CancellationToken ct)
            {
                CallCount++;
                if (_neverCompletes)
                {
                    return TimerDelay.Wait(60000, ct);
                }

                return Task.CompletedTask;
            }
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
