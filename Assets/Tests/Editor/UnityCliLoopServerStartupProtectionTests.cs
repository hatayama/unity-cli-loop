using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
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

            UnityCliLoopEditorSettingsData originalSettings = CloneSettings(UnityCliLoopEditorSettings.GetSettings());

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
                UnityCliLoopEditorSettings.SaveSettings(originalSettings);
                DomainReloadDetectionService.DeleteLockFile();
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
