using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI setup application service behavior.
    /// </summary>
    public class CliSetupApplicationServiceTests
    {
        [Test]
        public async Task InstallGlobalCliAsync_UsesBundledRequiredDispatcherVersion()
        {
            // Verifies that global Dispatcher installs track Core compatibility, not package release cadence.
            FakeProjectLocalCliInstaller projectLocalCliInstaller = new("3.0.0-beta.1");
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(),
                projectLocalCliInstaller,
                nativeCliInstaller);

            await service.InstallGlobalCliAsync(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.2",
                CancellationToken.None);

            Assert.That(nativeCliInstaller.InstalledVersion, Is.EqualTo("3.0.0-beta.1"));
        }

        [Test]
        public void GetGlobalCliInstallCommand_UsesBundledRequiredDispatcherVersion()
        {
            // Verifies that fallback manual commands point at the Dispatcher release required by bundled Core.
            FakeProjectLocalCliInstaller projectLocalCliInstaller = new("3.0.0-beta.1");
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(),
                projectLocalCliInstaller,
                nativeCliInstaller);

            NativeCliInstallCommand command = service.GetGlobalCliInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.2",
                false);

            Assert.That(command.ManualCommand, Is.EqualTo("install 3.0.0-beta.1"));
        }

        private sealed class FakeCliInstallationDetector : ICliInstallationDetector
        {
            public bool IsCliInstalled() => false;
            public string GetCachedCliVersion() => "";
            public string GetCachedCliExecutablePath() => "";
            public bool IsCheckCompleted() => true;
            public Task RefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;
            public Task ForceRefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;
            public void InvalidateCache() { }
        }

        private sealed class FakeProjectLocalCliInstaller : IProjectLocalCliInstaller
        {
            private readonly string _requiredDispatcherVersion;

            public FakeProjectLocalCliInstaller(string requiredDispatcherVersion)
            {
                _requiredDispatcherVersion = requiredDispatcherVersion;
            }

            public string DetectBundledRequiredDispatcherVersion()
            {
                return _requiredDispatcherVersion;
            }

            public CliInstallResult EnsureProjectLocalCliCurrent(string projectRoot, string packageVersion)
            {
                return new CliInstallResult(true, "");
            }
        }

        private sealed class FakeNativeCliInstaller : INativeCliInstaller
        {
            public string InstalledVersion { get; private set; }

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                return false;
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string packageVersion,
                CancellationToken ct)
            {
                InstalledVersion = packageVersion;
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public NativeCliInstallCommand GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string packageVersion,
                bool removeLegacyLaunchers)
            {
                return new NativeCliInstallCommand("sh", "-c true", $"install {packageVersion}");
            }
        }
    }
}
