using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Unity CLI Loop Server Startup Protection behavior.
    /// </summary>
    public class UnityCliLoopServerStartupProtectionTests
    {
        private UnityCliLoopEditorSessionStateService _sessionStateService;
        private UnityCliLoopEditorSessionStateSnapshot _originalSessionState;

        [SetUp]
        public void SetUp()
        {
            _sessionStateService = UnityCliLoopEditorSessionStateTestFactory.CreateService();
            _originalSessionState = UnityCliLoopEditorSessionStateTestFactory.CaptureSnapshot(_sessionStateService);
            _sessionStateService.ClearAll();
        }

        [TearDown]
        public void TearDown()
        {
            _originalSessionState.Restore(_sessionStateService);
        }

        [Test]
        public void ClearStartupProtection_ResetsProtectionWindow()
        {
            // Tests that the startup protection window can be cleared by recovery code.
            UnityCliLoopServerControllerService service = CreateControllerService();

            service.ActivateStartupProtection(60000);

            Assert.IsTrue(service.IsStartupProtectionActive(), "Startup protection should be active after activation");

            service.ClearStartupProtection();

            Assert.IsFalse(service.IsStartupProtectionActive(), "Startup protection should be cleared by recovery path");
        }

        [Test]
        public void OnBeforeAssemblyReload_ShouldClearStartupProtectionBeforeRecovery()
        {
            // Tests that assembly-reload recovery clears the startup protection window before shutdown.
            UnityCliLoopServerControllerService service = CreateControllerService();
            service.RegisterRecoveredServer(new TestServerInstance());
            service.ActivateStartupProtection(60000);

            Assert.IsTrue(service.IsStartupProtectionActive(), "Startup protection should be active before reload");

            service.OnBeforeAssemblyReload();

            Assert.IsFalse(
                service.IsStartupProtectionActive(),
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
        public void PrepareForServerShutdown_ShouldClearStartupProtectionBeforeShutdown()
        {
            // Tests that explicit shutdown clears the startup protection window before stopping.
            UnityCliLoopServerControllerService service = CreateControllerService();

            service.ActivateStartupProtection(60000);

            Assert.IsTrue(service.IsStartupProtectionActive(), "Startup protection should be active before shutdown");

            service.PrepareForServerShutdown();

            Assert.IsFalse(
                service.IsStartupProtectionActive(),
                "Shutdown path should clear startup protection so recovery can restart the server"
            );
        }

        private UnityCliLoopServerControllerService CreateControllerService()
        {
            return CreateControllerService(new TestDomainReloadLifecycle());
        }

        private UnityCliLoopServerControllerService CreateControllerService(TestDomainReloadLifecycle domainReloadLifecycle)
        {
            TestServerInstanceFactory serverInstanceFactory = new();
            UnityCliLoopServerLifecycleRegistryService lifecycleRegistry =
                new UnityCliLoopServerLifecycleRegistryService();
            return new UnityCliLoopServerControllerService(
                serverInstanceFactory,
                lifecycleRegistry,
                new DomainReloadDetectionFileService(_sessionStateService),
                _sessionStateService,
                new TestReadinessProbe(),
                domainReloadLifecycle);
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
