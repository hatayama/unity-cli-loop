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
            UnityCliLoopEditorSettingsService editorSettingsService =
                UnityCliLoopEditorSettingsTestFactory.CreateService();

            UnityCliLoopEditorSettingsData originalSettings = CloneSettings(editorSettingsService.GetSettings());

            try
            {
                service.RegisterRecoveredServer(new TestServerInstance());
                service.ActivateStartupProtection(60000);

                Assert.IsTrue(service.IsStartupProtectionActive(), "Startup protection should be active before reload");

                service.OnBeforeAssemblyReload();

                Assert.IsFalse(
                    service.IsStartupProtectionActive(),
                    "Assembly reload recovery should clear startup protection so the server can restart"
                );
            }
            finally
            {
                editorSettingsService.SaveSettings(originalSettings);
                service.ClearStartupProtection();
            }
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

        private static UnityCliLoopEditorSettingsData CloneSettings(UnityCliLoopEditorSettingsData settings)
        {
            string json = UnityEngine.JsonUtility.ToJson(settings);
            return UnityEngine.JsonUtility.FromJson<UnityCliLoopEditorSettingsData>(json);
        }

        private static UnityCliLoopServerControllerService CreateControllerService()
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
                new TestReadinessProbe());
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
            public System.Threading.Tasks.Task ProbeAsync(System.Threading.CancellationToken ct)
            {
                return System.Threading.Tasks.Task.CompletedTask;
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
