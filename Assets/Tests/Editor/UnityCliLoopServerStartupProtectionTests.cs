using NUnit.Framework;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Server Startup Protection behavior.
    /// </summary>
    public class UnityCliLoopServerStartupProtectionTests
    {
        private UnityCliLoopSessionFlagsRepository _sessionFlagsRepository;
        private UnityCliLoopCompileSessionLifecycleService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;
        private InMemoryDomainReloadDurationStore _reloadDurationStore;

        [SetUp]
        public void SetUp()
        {
            _sessionFlagsRepository = UnityCliLoopEditorSessionStateTestFactory.CreateSessionFlagsRepository();
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateCompileSessionLifecycleService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot();
            UnityCliLoopEditorSessionStateTestFactory.ClearAll();
            _reloadDurationStore = new InMemoryDomainReloadDurationStore();
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore();
        }

        [Test]
        public void ClearStartupProtection_ResetsProtectionWindow()
        {
            // Tests that the startup protection window can be cleared by recovery code.
            UnityCliLoopServerStartupProtectionService service = new();

            service.ActivateStartupProtection(60000);

            Assert.IsTrue(service.IsStartupProtectionActive(), "Startup protection should be active after activation");

            service.ClearStartupProtection();

            Assert.IsFalse(service.IsStartupProtectionActive(), "Startup protection should be cleared by recovery path");
        }

        [Test]
        public void OnBeforeAssemblyReload_ShouldClearStartupProtectionBeforeRecovery()
        {
            // Tests that assembly-reload recovery clears the startup protection window before shutdown.
            UnityCliLoopServerStartupProtectionService startupProtectionService = new();
            UnityCliLoopServerControllerService service = CreateControllerService(
                startupProtectionService: startupProtectionService);
            service.RegisterRecoveredServer(new TestServerInstance());
            startupProtectionService.ActivateStartupProtection(60000);

            Assert.IsTrue(startupProtectionService.IsStartupProtectionActive(), "Startup protection should be active before reload");

            service.OnBeforeAssemblyReload();

            Assert.IsFalse(
                startupProtectionService.IsStartupProtectionActive(),
                "Assembly reload recovery should clear startup protection so the server can restart"
            );
        }

        [Test]
        public void OnBeforeAssemblyReload_ShouldPrepareDomainReloadLifecycle()
        {
            // Tests that bundled server-scoped services are reset through the domain-reload lifecycle hook.
            TestDomainReloadLifecycle domainReloadLifecycle = new();
            UnityCliLoopServerControllerService service = CreateControllerService(domainReloadLifecycle);

            service.OnBeforeAssemblyReload();

            Assert.That(domainReloadLifecycle.PrepareCallCount, Is.EqualTo(1));
        }

        [Test]
        public async Task StopServerWithUseCaseAsync_ShouldClearStartupProtectionBeforeShutdown()
        {
            // Tests that explicit shutdown clears the startup protection window before stopping.
            UnityCliLoopServerStartupProtectionService startupProtectionService = new();
            UnityCliLoopServerControllerService service = CreateControllerService(
                startupProtectionService: startupProtectionService);

            startupProtectionService.ActivateStartupProtection(60000);

            Assert.IsTrue(startupProtectionService.IsStartupProtectionActive(), "Startup protection should be active before shutdown");

            await service.StopServerWithUseCaseAsync();

            Assert.IsFalse(
                startupProtectionService.IsStartupProtectionActive(),
                "Shutdown path should clear startup protection so recovery can restart the server"
            );
        }

        [Test]
        public void OnBeforeAssemblyReload_RecordsTheReloadStart()
        {
            // Tests that the before-reload hook starts measuring the domain reload outside Play Mode.
            UnityCliLoopServerControllerService service = CreateControllerService();

            service.OnBeforeAssemblyReload();

            DomainReloadDurationState state = _reloadDurationStore.Load();
            Assert.That(state.PendingStartUtcTicks.HasValue, Is.True);
            Assert.That(state.PendingIsMeasured, Is.True);
        }

        [Test]
        public void OnBeforeAssemblyReload_WhenStoppingTheServerThrows_StillRecordsTheReloadStart()
        {
            // Tests that a failed server stop, after which Unity still reloads, does not leave the reload unrecorded.
            UnityCliLoopServerControllerService service = CreateControllerService();
            service.RegisterRecoveredServer(new ThrowingStopServerInstance());

            Assert.Throws<System.InvalidOperationException>(() => service.OnBeforeAssemblyReload());

            Assert.That(_reloadDurationStore.Load().PendingStartUtcTicks.HasValue, Is.True);
        }

        [Test]
        public async Task OnAfterAssemblyReload_FinishesTheReloadStartedBeforeIt()
        {
            // Tests that the after-reload hook records the reload's duration before the recovery it schedules runs.
            // A manual stop keeps that recovery from starting a server.
            _sessionFlagsRepository.MarkServerManuallyStopped();
            new DomainReloadDurationMonitor(_reloadDurationStore, () => false)
                .RecordReloadStarted(System.DateTime.UtcNow.AddSeconds(-1));
            UnityCliLoopServerControllerService service = CreateControllerService();

            service.OnAfterAssemblyReload();

            DomainReloadDurationState state = _reloadDurationStore.Load();
            Assert.That(state.PendingStartUtcTicks.HasValue, Is.False);
            Assert.That(state.RecentReloads, Has.Length.EqualTo(1));
            Assert.That(state.RecentReloads[0].Seconds, Is.GreaterThan(0d));
            Task recoveryTask = service.RecoveryTask;
            if (recoveryTask != null)
            {
                await recoveryTask;
                Assert.That(recoveryTask.Status, Is.EqualTo(TaskStatus.RanToCompletion));
            }
        }

        private UnityCliLoopServerControllerService CreateControllerService(
            TestDomainReloadLifecycle domainReloadLifecycle = null,
            UnityCliLoopServerStartupProtectionService startupProtectionService = null)
        {
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            DomainReloadDetectionFileService domainReloadDetectionService =
                new DomainReloadDetectionFileService(
                    _sessionFlagsRepository,
                    new UnityCliLoopPendingCompileSessionRepository(),
                    _sessionStateService);
            UnityCliLoopServerStartupService startupService = new(
                serverInstanceFactory,
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
            UnityCliLoopServerReadinessService readinessService = new(
                lifecycleRegistry,
                new TestReadinessProbe());
            UnityCliLoopServerRecoveryTrackingService recoveryTrackingService = new(_sessionFlagsRepository);
            return new UnityCliLoopServerControllerService(
                serverInstanceFactory,
                lifecycleRegistry,
                domainReloadDetectionService,
                _sessionFlagsRepository,
                initializationUseCase,
                shutdownUseCase,
                sessionRecoveryService,
                domainReloadRecoveryUseCase,
                UnityCliLoopToolRegistrarTestFactory.Create(() => System.Array.Empty<IUnityCliLoopTool>()),
                readinessService,
                startupProtectionService ?? new UnityCliLoopServerStartupProtectionService(),
                recoveryTrackingService,
                domainReloadLifecycle ?? new TestDomainReloadLifecycle(),
                new DomainReloadDurationMonitor(_reloadDurationStore, () => false));
        }

        /// <summary>
        /// Test support type that makes readiness probing deterministic and side-effect free.
        /// </summary>
        private sealed class TestReadinessProbe : IUnityCliLoopServerReadinessProbe
        {
            public System.Threading.Tasks.Task ProbeAsync(System.Threading.CancellationToken ct)
            {
                return System.Threading.Tasks.Task.CompletedTask;
            }
        }

        /// <summary>
        /// Test support type that records domain reload lifecycle calls.
        /// </summary>
        private sealed class TestDomainReloadLifecycle : IUnityCliLoopServerDomainReloadLifecycle
        {
            public int PrepareCallCount { get; private set; }

            public void PrepareForDomainReload()
            {
                PrepareCallCount++;
            }
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
        /// Test support type for a running server whose stop fails.
        /// </summary>
        private sealed class ThrowingStopServerInstance : IUnityCliLoopServerInstance
        {
            public bool IsRunning => true;

            public string Endpoint => "test";

            public void StartServer()
            {
            }

            public void StopServer()
            {
                throw new System.InvalidOperationException("Stopping the test server failed.");
            }

            public void Dispose()
            {
            }
        }

        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class TestServerInstance : IUnityCliLoopServerInstance
        {
            public bool IsRunning => false;

            public string Endpoint => "test";

            public void StartServer()
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
