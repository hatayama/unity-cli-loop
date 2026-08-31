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
    /// Test fixture that verifies Unity CLI Loop Server Controller recovery behavior.
    /// </summary>
    public class UnityCliLoopServerControllerRecoveryTests
    {
        private UnityCliLoopSessionFlagsRepository _sessionFlagsRepository;
        private UnityCliLoopCompileSessionLifecycleService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;

        [SetUp]
        public void SetUp()
        {
            _sessionFlagsRepository = UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateCompileSessionLifecycleService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore();
        }

        [Test]
        public void ScheduleStartupRecovery_WhenCalled_ExposesRecoveryTaskBeforeDeferredActionRuns()
        {
            // Tests that deferred startup recovery exposes its pending task before execution.
            System.Action scheduledAction = null;
            bool recoveryExecuted = false;
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

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
        public async Task ScheduleStartupRecovery_WhenRecoveryThrowsSynchronously_CompletesTaskAndClearsRecoveryTask()
        {
            // Tests that synchronous startup recovery failures log, complete, and clear the tracked task.
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                "[UnityCliLoop] Failed to restore server: restore failed");
            System.Action scheduledAction = null;
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

            Task recoveryTask = service.ScheduleStartupRecovery(
                action => scheduledAction = action,
                () => throw new System.InvalidOperationException("restore failed"));

            scheduledAction();

            Assert.That(recoveryTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(recoveryTask.IsFaulted, Is.False);
            Assert.That(recoveryTask.IsCanceled, Is.False);
            Assert.That(service.RecoveryTask, Is.Null);
            await recoveryTask;
        }

        [Test]
        public async Task ScheduleStartupRecovery_WhenRecoveryIsAsync_KeepsTaskIncompleteUntilRecoveryCompletes()
        {
            // Tests that asynchronous startup recovery remains pending until its restore task completes.
            System.Action scheduledAction = null;
            TaskCompletionSource<bool> recoveryCompletionSource = new();
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

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
        public async Task ScheduleStartupRecovery_WhenRecoveryIsCanceled_CompletesTaskAndClearsRecoveryTask()
        {
            // Tests that startup recovery cancellation completes as an activity-finished signal.
            System.Action scheduledAction = null;
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

            Task recoveryTask = service.ScheduleStartupRecovery(
                action => scheduledAction = action,
                () => Task.FromCanceled(new CancellationToken(true)));

            scheduledAction();

            Assert.That(recoveryTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(recoveryTask.IsFaulted, Is.False);
            Assert.That(recoveryTask.IsCanceled, Is.False);
            Assert.That(service.RecoveryTask, Is.Null);
            await recoveryTask;
        }

        [Test]
        public async Task ScheduleTrackedRecovery_WhenRecoveryAlreadyRunning_ReturnsCurrentTaskWithoutStartingAnotherRecovery()
        {
            // Tests that duplicate tracked recovery triggers join the active recovery instead of starting a second loop.
            int recoveryStartCount = 0;
            TaskCompletionSource<bool> firstRecoveryCompletionSource = new();
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

            Task firstTask = service.ScheduleTrackedRecovery(() =>
            {
                recoveryStartCount++;
                return firstRecoveryCompletionSource.Task;
            });
            Task secondTask = service.ScheduleTrackedRecovery(() =>
            {
                recoveryStartCount++;
                return Task.CompletedTask;
            });

            Assert.That(secondTask, Is.SameAs(firstTask));
            Assert.That(recoveryStartCount, Is.EqualTo(1));

            firstRecoveryCompletionSource.SetResult(true);
            await firstTask;
        }

        [Test]
        public async Task ScheduleStartupRecovery_WhenTrackedRecoveryIsRunning_ReturnsCurrentTaskWithoutSchedulingStartupRecovery()
        {
            // Tests that startup recovery joins an active tracked recovery without registering a delay call.
            int scheduledActionCount = 0;
            TaskCompletionSource<bool> trackedRecoveryCompletionSource = new();
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

            Task trackedTask = service.ScheduleTrackedRecovery(() => trackedRecoveryCompletionSource.Task);
            Task startupTask = service.ScheduleStartupRecovery(
                action => scheduledActionCount++,
                () => Task.CompletedTask);

            Assert.That(startupTask, Is.SameAs(trackedTask));
            Assert.That(scheduledActionCount, Is.EqualTo(0));

            trackedRecoveryCompletionSource.SetResult(true);
            await trackedTask;
        }

        [Test]
        public void ScheduleStartupRecovery_WhenStartupRecoveryIsPending_ReturnsCurrentTaskWithoutSchedulingAnotherRecovery()
        {
            // Tests that duplicate startup recovery triggers join the active startup placeholder.
            int scheduledActionCount = 0;
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

            Task firstTask = service.ScheduleStartupRecovery(
                action => scheduledActionCount++,
                () => Task.CompletedTask);
            Task secondTask = service.ScheduleStartupRecovery(
                action => scheduledActionCount++,
                () => Task.CompletedTask);

            Assert.That(secondTask, Is.SameAs(firstTask));
            Assert.That(scheduledActionCount, Is.EqualTo(1));
        }

        [Test]
        public async Task ScheduleTrackedRecovery_WhenStartupRecoveryIsPending_ReplacesStartupPlaceholderWithTrackedRecovery()
        {
            // Tests that after-domain-reload recovery is not dropped behind startup recovery.
            System.Action scheduledAction = null;
            int trackedRecoveryStartCount = 0;
            TaskCompletionSource<bool> startupRecoveryCompletionSource = new();
            TaskCompletionSource<bool> trackedRecoveryCompletionSource = new();
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

            Task startupTask = service.ScheduleStartupRecovery(
                action => scheduledAction = action,
                () => startupRecoveryCompletionSource.Task);
            Task trackedTask = service.ScheduleTrackedRecovery(() =>
            {
                trackedRecoveryStartCount++;
                return trackedRecoveryCompletionSource.Task;
            });

            Assert.That(trackedTask, Is.Not.SameAs(startupTask));
            Assert.That(trackedTask, Is.SameAs(service.RecoveryTask));
            Assert.That(trackedRecoveryStartCount, Is.EqualTo(1));

            scheduledAction();
            startupRecoveryCompletionSource.SetResult(true);
            await startupTask;
            Assert.That(service.RecoveryTask, Is.SameAs(trackedTask));

            trackedRecoveryCompletionSource.SetResult(true);
            await trackedTask;
            Assert.That(service.RecoveryTask, Is.Null);
        }

        [Test]
        public async Task ScheduleTrackedRecovery_WhenPreviousRecoveryCompleted_StartsNewRecovery()
        {
            // Tests that completed recoveries do not block a later recovery trigger.
            int recoveryStartCount = 0;
            TaskCompletionSource<bool> firstCompletionSource = new();
            TaskCompletionSource<bool> secondCompletionSource = new();
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService();

            Task firstTask = service.ScheduleTrackedRecovery(() =>
            {
                recoveryStartCount++;
                return firstCompletionSource.Task;
            });
            firstCompletionSource.SetResult(true);
            await firstTask;
            Task secondTask = service.ScheduleTrackedRecovery(() =>
            {
                recoveryStartCount++;
                return secondCompletionSource.Task;
            });
            secondCompletionSource.SetResult(true);
            await secondTask;

            Assert.That(secondTask, Is.Not.SameAs(firstTask));
            Assert.That(recoveryStartCount, Is.EqualTo(2));
        }

        [Test]
        public async Task ScheduleTrackedRecovery_WhenPreviousRecoveryGaveUp_CompletesTaskAndStartsNewRecovery()
        {
            // Tests that public recovery tracking completes after give-up and does not block later recovery.
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                "[UnityCliLoop] Unity CLI Loop server recovery failed before the bridge became ready. first failure");
            _sessionFlagsRepository.MarkServerStarted();
            int firstRecoveryAttempts = 0;
            bool secondRecoveryStarted = false;
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService(
                (delayMilliseconds, ct) => Task.CompletedTask);

            Task firstTask = service.ScheduleTrackedRecovery(() =>
            {
                firstRecoveryAttempts++;
                throw new System.TimeoutException("first failure");
            });
            await firstTask;
            Task secondTask = service.ScheduleTrackedRecovery(() =>
            {
                secondRecoveryStarted = true;
                TaskCompletionSource<bool> completionSource = new();
                completionSource.SetResult(true);
                return completionSource.Task;
            });
            await secondTask;

            Assert.That(firstTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            Assert.That(firstTask.IsFaulted, Is.False);
            Assert.That(firstTask.IsCanceled, Is.False);
            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.False);
            Assert.That(secondTask, Is.Not.SameAs(firstTask));
            Assert.That(
                firstRecoveryAttempts,
                Is.EqualTo(UnityCliLoopServerConfig.RECOVERY_RETRY_DELAYS_MS.Length + 1));
            Assert.That(secondRecoveryStarted, Is.True);
        }

        [Test]
        public async Task StartRecoveryIfNeededAsync_WhenReadinessSucceeds_ShouldPublishServerStartedEvent()
        {
            // Tests that recovery publishes startup only after the readiness probe succeeds.
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            TestReadinessProbe readinessProbe = new();
            int serverStartedCount = 0;
            lifecycleRegistry.ServerStarted += () => serverStartedCount++;
            UnityCliLoopServerControllerService service = CreateControllerService(
                readinessProbe: readinessProbe,
                serverInstanceFactory: serverInstanceFactory,
                lifecycleRegistry: lifecycleRegistry,
                isReadinessProbeBlocked: () => false);

            await service.StartRecoveryIfNeededAsync(isAfterCompile: false, CancellationToken.None);

            Assert.That(readinessProbe.CallCount, Is.EqualTo(1));
            Assert.That(serverStartedCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StartRecoveryIfNeededAsync_WhenEditorIsBusy_ShouldDelayReadinessProbeUntilIdle()
        {
            // Tests that recovery does not spend readiness timeout while Unity is still compiling or updating.
            bool editorIsBusy = true;
            int delayCallCount = 0;
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            TestReadinessProbe readinessProbe = new();
            int serverStartedCount = 0;
            lifecycleRegistry.ServerStarted += () => serverStartedCount++;
            UnityCliLoopServerControllerService service = CreateControllerService(
                readinessProbe: readinessProbe,
                serverInstanceFactory: serverInstanceFactory,
                lifecycleRegistry: lifecycleRegistry,
                isReadinessProbeBlocked: () => editorIsBusy,
                waitBeforeReadinessRetryAsync: (delayMilliseconds, ct) =>
                {
                    delayCallCount++;
                    Assert.That(readinessProbe.CallCount, Is.EqualTo(0));
                    editorIsBusy = false;
                    return Task.CompletedTask;
                });

            await service.StartRecoveryIfNeededAsync(isAfterCompile: false, CancellationToken.None);

            Assert.That(delayCallCount, Is.EqualTo(1));
            Assert.That(readinessProbe.CallCount, Is.EqualTo(1));
            Assert.That(serverStartedCount, Is.EqualTo(1));
        }

        [Test]
        public void StartRecoveryIfNeededAsync_WhenPartiallyCreatedServerDisposeFails_ShouldSurfaceDisposeFailure()
        {
            // Tests that recovery cleanup does not hide a failed partially-created server disposal.
            using CancellationTokenSource cancellationTokenSource = new();
            TestServerInstance partiallyCreatedServer = new(
                throwOnStart: true,
                throwOnDispose: true,
                onDispose: cancellationTokenSource.Cancel);
            TestServerInstanceFactory serverInstanceFactory = new(
                serverInstance: partiallyCreatedServer);
            UnityCliLoopServerControllerService service = CreateControllerService(
                serverInstanceFactory: serverInstanceFactory);

            System.InvalidOperationException exception =
                Assert.ThrowsAsync<System.InvalidOperationException>(
                    async () => await service.StartRecoveryIfNeededAsync(
                        isAfterCompile: false,
                        cancellationTokenSource.Token));

            Assert.That(exception.Message, Is.EqualTo("dispose failed"));
        }

        [Test]
        public void StartRecoveryIfNeededAsync_WhenEditorNeverBecomesIdle_ShouldFailWithoutReadinessProbe()
        {
            // Tests that recovery does not hang forever when Unity never leaves compile or update state.
            int delayCallCount = 0;
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            TestReadinessProbe readinessProbe = new();
            int serverStartedCount = 0;
            lifecycleRegistry.ServerStarted += () => serverStartedCount++;
            UnityCliLoopServerControllerService service = CreateControllerService(
                readinessProbe: readinessProbe,
                serverInstanceFactory: serverInstanceFactory,
                lifecycleRegistry: lifecycleRegistry,
                isReadinessProbeBlocked: () => true,
                waitBeforeReadinessRetryAsync: (delayMilliseconds, ct) =>
                {
                    delayCallCount++;
                    return Task.CompletedTask;
                },
                readinessIdleTimeoutMilliseconds: 1);

            System.InvalidOperationException exception =
                Assert.ThrowsAsync<System.InvalidOperationException>(
                    async () => await service.StartRecoveryIfNeededAsync(
                        isAfterCompile: false,
                        CancellationToken.None));

            Assert.That(exception.Message, Does.Contain("Unity editor idle"));
            Assert.That(delayCallCount, Is.EqualTo(1));
            Assert.That(readinessProbe.CallCount, Is.EqualTo(0));
            Assert.That(serverStartedCount, Is.EqualTo(0));
        }

        [Test]
        public async Task ProbeReadinessWithTimeoutAsync_WhenProbeDoesNotComplete_ThrowsTimeout()
        {
            // Tests that readiness probing fails fast instead of leaving startup state stuck forever.
            TestReadinessProbe readinessProbe = new(neverCompletes: true);
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            UnityCliLoopServerReadinessService service = new(
                lifecycleRegistry,
                readinessProbe);

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

        [Test]
        public async Task ExecuteTrackedRecoveryAsync_WhenRecoveryFailsOnce_RetriesAfterBackoffAndSucceeds()
        {
            // Tests that a transient recovery failure (e.g. readiness timeout during a heavy import)
            // is retried with backoff instead of leaving the server down until the next domain reload.
            System.Collections.Generic.List<int> recordedWaits = new();
            int recoveryAttempts = 0;
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService(
                (delayMilliseconds, ct) =>
                {
                    recordedWaits.Add(delayMilliseconds);
                    return Task.CompletedTask;
                });

            await service.ExecuteTrackedRecoveryAsync(() =>
            {
                recoveryAttempts++;
                if (recoveryAttempts == 1)
                {
                    throw new System.TimeoutException("readiness probe timed out");
                }

                return Task.CompletedTask;
            });

            Assert.That(recoveryAttempts, Is.EqualTo(2));
            Assert.That(recordedWaits, Is.EqualTo(new[]
            {
                UnityCliLoopServerConfig.RECOVERY_RETRY_DELAYS_MS[0]
            }));
        }

        [Test]
        public void ExecuteTrackedRecoveryAsync_WhenRecoveryKeepsFailing_GivesUpAfterAllRetriesAndClearsSession()
        {
            // Tests that persistent recovery failure exhausts the full backoff schedule before
            // surfacing the error and clearing the server session.
            _sessionFlagsRepository.MarkServerStarted();
            System.Collections.Generic.List<int> recordedWaits = new();
            int recoveryAttempts = 0;
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService(
                (delayMilliseconds, ct) =>
                {
                    recordedWaits.Add(delayMilliseconds);
                    return Task.CompletedTask;
                });
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                "[UnityCliLoop] Unity CLI Loop server recovery failed before the bridge became ready. readiness probe timed out");

            System.InvalidOperationException exception =
                Assert.ThrowsAsync<System.InvalidOperationException>(async () =>
                    await service.ExecuteTrackedRecoveryAsync(() =>
                    {
                        recoveryAttempts++;
                        throw new System.TimeoutException("readiness probe timed out");
                    }));

            Assert.That(exception.Message, Does.Contain("recovery failed"));
            Assert.That(recoveryAttempts, Is.EqualTo(UnityCliLoopServerConfig.RECOVERY_RETRY_DELAYS_MS.Length + 1));
            Assert.That(
                recordedWaits,
                Is.EqualTo(UnityCliLoopServerConfig.RECOVERY_RETRY_DELAYS_MS));
            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.False);
        }

        [Test]
        public async Task ExecuteTrackedRecoveryAsync_WhenServerManuallyStoppedDuringBackoff_AbandonsRetry()
        {
            // Tests that an explicit Stop Server during the retry backoff wins over automatic recovery.
            int recoveryAttempts = 0;
            UnityCliLoopServerRecoveryTrackingService service = CreateRecoveryTrackingService(
                (delayMilliseconds, ct) =>
                {
                    _sessionFlagsRepository.MarkServerManuallyStopped();
                    return Task.CompletedTask;
                });

            await service.ExecuteTrackedRecoveryAsync(() =>
            {
                recoveryAttempts++;
                throw new System.TimeoutException("readiness probe timed out");
            });

            Assert.That(recoveryAttempts, Is.EqualTo(1));
        }

        [Test]
        public async Task RestoreServerStateIfNeeded_WhenServerWasManuallyStopped_ShouldSkipStartupRecovery()
        {
            // Tests that explicit Stop Server is preserved when startup recovery runs after Domain Reload.
            _sessionFlagsRepository.MarkServerManuallyStopped();
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            UnityCliLoopServerControllerService service = CreateControllerService(
                serverInstanceFactory: serverInstanceFactory,
                lifecycleRegistry: lifecycleRegistry);

            await service.RestoreServerStateIfNeeded();

            Assert.That(serverInstanceFactory.LastCreated, Is.Null);
        }

        [Test]
        public void RestoreServerStateIfNeeded_WhenStartupProtectionActiveWithoutServer_ShouldSurfaceRecoveryFailure()
        {
            // Tests that startup recovery reports failure when protection suppresses recovery without a server.
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerStartupProtectionService startupProtectionService = new();
            startupProtectionService.ActivateStartupProtection(60000);
            UnityCliLoopServerControllerService service = CreateControllerService(
                serverInstanceFactory: serverInstanceFactory,
                startupProtectionService: startupProtectionService);

            System.InvalidOperationException exception =
                Assert.ThrowsAsync<System.InvalidOperationException>(
                    async () => await service.RestoreServerStateIfNeeded());

            Assert.That(
                exception.Message,
                Is.EqualTo("Unity CLI Loop server recovery finished, but no running server instance is available."));
            Assert.That(serverInstanceFactory.LastCreated, Is.Null);
        }

        [Test]
        public async Task StopServerWithUseCaseAsync_WhenStoppedByUser_ShouldMarkManualStop()
        {
            // Tests that the manual Stop Server path records explicit user stop intent.
            UnityCliLoopServerControllerService service = CreateControllerService();
            TestServerInstance runningServer = new();
            runningServer.StartServer();
            service.RegisterRecoveredServer(runningServer);

            await service.StopServerWithUseCaseAsync();

            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.False);
            Assert.That(_sessionFlagsRepository.GetIsServerManuallyStopped(), Is.True);
        }

        [Test]
        public async Task StartServerWithUseCaseAsync_WhenRestartCleanupStartFails_ShouldNotMarkManualStop()
        {
            // Tests that internal restart cleanup is not mistaken for explicit user stop intent.
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                "Server startup failed: Failed to start server: start failed");
            TestServerInstanceFactory serverInstanceFactory = new(throwOnCreate: true);
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            UnityCliLoopServerControllerService service = CreateControllerService(
                serverInstanceFactory: serverInstanceFactory,
                lifecycleRegistry: lifecycleRegistry);
            TestServerInstance runningServer = new();
            runningServer.StartServer();
            service.RegisterRecoveredServer(runningServer);

            await service.StartServerWithUseCaseAsync();

            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.False);
            Assert.That(_sessionFlagsRepository.GetIsServerManuallyStopped(), Is.False);
        }

        [Test]
        public async Task StartServerWithUseCaseAsync_WhenRestartCleanupStopFails_ShouldNotStartNewServer()
        {
            // Tests that restart cleanup failure does not get hidden behind a second startup attempt.
            UnityEngine.TestTools.LogAssert.Expect(
                UnityEngine.LogType.Error,
                "Server shutdown failed: Failed to stop server: dispose failed");
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            UnityCliLoopServerControllerService service = CreateControllerService(
                serverInstanceFactory: serverInstanceFactory,
                lifecycleRegistry: lifecycleRegistry);
            TestServerInstance runningServer = new(throwOnDispose: true);
            runningServer.StartServer();
            service.RegisterRecoveredServer(runningServer);

            await service.StartServerWithUseCaseAsync();

            Assert.That(serverInstanceFactory.LastCreated, Is.Null);
            Assert.That(_sessionFlagsRepository.GetIsServerRunning(), Is.True);
            Assert.That(_sessionFlagsRepository.GetIsServerManuallyStopped(), Is.False);
        }

        private UnityCliLoopServerControllerService CreateControllerService(
            TestReadinessProbe readinessProbe = null,
            System.Func<int, CancellationToken, Task> waitBeforeRecoveryRetryAsync = null,
            TestServerInstanceFactory serverInstanceFactory = null,
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry = null,
            System.Func<bool> isReadinessProbeBlocked = null,
            System.Func<int, CancellationToken, Task> waitBeforeReadinessRetryAsync = null,
            int readinessIdleTimeoutMilliseconds = UnityCliLoopServerConfig.READINESS_PROBE_TIMEOUT_MS,
            UnityCliLoopServerStartupProtectionService startupProtectionService = null)
        {
            TestServerInstanceFactory effectiveServerInstanceFactory =
                serverInstanceFactory ?? new TestServerInstanceFactory();
            UnityCliLoopServerLifecycleRegistryService effectiveLifecycleRegistry =
                lifecycleRegistry ?? new UnityCliLoopServerLifecycleRegistryService();
            DomainReloadDetectionFileService domainReloadDetectionService =
                CreateDomainReloadDetectionService();
            UnityCliLoopServerStartupService startupService = new(
                effectiveServerInstanceFactory,
                _sessionFlagsRepository);
            UnityCliLoopServerInitializationUseCase initializationUseCase = new(
                new EditorSecurityValidationService(),
                startupService);
            UnityCliLoopServerShutdownUseCase shutdownUseCase = new(startupService);
            SessionRecoveryService sessionRecoveryService = new(
                domainReloadDetectionService,
                _sessionFlagsRepository);
            DomainReloadRecoveryUseCase domainReloadRecoveryUseCase = new(
                sessionRecoveryService,
                domainReloadDetectionService,
                _sessionFlagsRepository);
            TestReadinessProbe effectiveReadinessProbe = readinessProbe ?? new TestReadinessProbe();
            UnityCliLoopServerReadinessService readinessService = new(
                effectiveLifecycleRegistry,
                effectiveReadinessProbe,
                isReadinessProbeBlocked,
                waitBeforeReadinessRetryAsync,
                readinessIdleTimeoutMilliseconds);
            UnityCliLoopServerStartupProtectionService effectiveStartupProtectionService =
                startupProtectionService ?? new UnityCliLoopServerStartupProtectionService();
            UnityCliLoopServerRecoveryTrackingService recoveryTrackingService = CreateRecoveryTrackingService(
                waitBeforeRecoveryRetryAsync);
            return new UnityCliLoopServerControllerService(
                effectiveServerInstanceFactory,
                effectiveLifecycleRegistry,
                domainReloadDetectionService,
                _sessionFlagsRepository,
                initializationUseCase,
                shutdownUseCase,
                sessionRecoveryService,
                domainReloadRecoveryUseCase,
                UnityCliLoopToolRegistrarTestFactory.Create(() => System.Array.Empty<IUnityCliLoopTool>()),
                readinessService,
                effectiveStartupProtectionService,
                recoveryTrackingService,
                new TestDomainReloadLifecycle());
        }

        private UnityCliLoopServerRecoveryTrackingService CreateRecoveryTrackingService(
            System.Func<int, CancellationToken, Task> waitBeforeRecoveryRetryAsync = null)
        {
            return new UnityCliLoopServerRecoveryTrackingService(
                _sessionFlagsRepository,
                waitBeforeRecoveryRetryAsync);
        }

        private DomainReloadDetectionFileService CreateDomainReloadDetectionService()
        {
            return new DomainReloadDetectionFileService(
                _sessionFlagsRepository,
                new UnityCliLoopPendingCompileSessionRepository(),
                _sessionStateService);
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
        /// Test support type that keeps domain reload lifecycle behavior side-effect free.
        /// </summary>
        private sealed class TestDomainReloadLifecycle : IUnityCliLoopServerDomainReloadLifecycle
        {
            public void PrepareForDomainReload()
            {
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstanceFactory : IUnityCliLoopServerInstanceFactory
        {
            private readonly bool _throwOnCreate;
            private readonly TestServerInstance _serverInstance;

            public TestServerInstanceFactory(
                bool throwOnCreate = false,
                TestServerInstance serverInstance = null)
            {
                _throwOnCreate = throwOnCreate;
                _serverInstance = serverInstance;
            }

            public TestServerInstance LastCreated { get; private set; }

            public IUnityCliLoopServerInstance Create()
            {
                if (_throwOnCreate)
                {
                    throw new System.InvalidOperationException("start failed");
                }

                LastCreated = _serverInstance ?? new TestServerInstance();
                return LastCreated;
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstance : IUnityCliLoopServerInstance
        {
            private readonly bool _throwOnStart;
            private readonly bool _throwOnDispose;
            private readonly System.Action _onDispose;

            public TestServerInstance(
                bool throwOnDispose = false,
                bool throwOnStart = false,
                System.Action onDispose = null)
            {
                _throwOnStart = throwOnStart;
                _throwOnDispose = throwOnDispose;
                _onDispose = onDispose;
            }

            public bool IsRunning { get; private set; }

            public string Endpoint => "test";

            public void StartServer()
            {
                if (_throwOnStart)
                {
                    throw new System.InvalidOperationException("start failed");
                }

                IsRunning = true;
            }

            public void StopServer()
            {
                IsRunning = false;
            }

            public void Dispose()
            {
                _onDispose?.Invoke();

                if (_throwOnDispose)
                {
                    throw new System.InvalidOperationException("dispose failed");
                }

                IsRunning = false;
            }
        }
    }
}
