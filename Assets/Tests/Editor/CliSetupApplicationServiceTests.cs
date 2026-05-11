using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI setup application service behavior.
    /// </summary>
    public class CliSetupApplicationServiceTests
    {
        [Test]
        public async Task InstallGlobalCliAsync_UsesMinimumRequiredCliReleaseTag()
        {
            // Verifies that manual installs target the independent CLI release stream.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(null),
                nativeCliInstaller);

            await service.InstallGlobalCliAsync(RuntimePlatform.OSXEditor, CancellationToken.None);

            Assert.That(
                nativeCliInstaller.InstalledVersion,
                Is.EqualTo(CliConstants.CLI_RELEASE_TAG_PREFIX + CliConstants.MINIMUM_REQUIRED_CLI_VERSION));
        }

        [Test]
        public void GetGlobalCliInstallCommand_UsesMinimumRequiredCliReleaseTag()
        {
            // Verifies that fallback manual commands point at the independent CLI release stream.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(null),
                nativeCliInstaller);

            NativeCliInstallCommand command = service.GetGlobalCliInstallCommand(
                RuntimePlatform.OSXEditor,
                false);

            Assert.That(
                command.ManualCommand,
                Is.EqualTo("install " + CliConstants.CLI_RELEASE_TAG_PREFIX + CliConstants.MINIMUM_REQUIRED_CLI_VERSION));
        }

        [Test]
        public async Task EnsureGlobalCliCurrentAsync_WhenInstalledVersionSatisfiesMinimum_SkipsInstall()
        {
            // Verifies that startup does not download when the global CLI already satisfies the package minimum.
            FakeNativeCliInstaller nativeCliInstaller = new();
            FakeCliInstallationDetector detector = new(CliConstants.MINIMUM_REQUIRED_CLI_VERSION);
            CliSetupApplicationService service = new(detector, nativeCliInstaller);

            CliInstallResult result = await service.EnsureGlobalCliCurrentAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(nativeCliInstaller.InstallCount, Is.EqualTo(0));
            Assert.That(detector.ForceRefreshCount, Is.EqualTo(1));
        }

        [Test]
        public async Task EnsureGlobalCliCurrentAsync_WhenInstalledVersionIsTooOld_InstallsMinimumRelease()
        {
            // Verifies that startup upgrades the global CLI to the minimum CLI release tag.
            FakeNativeCliInstaller nativeCliInstaller = new();
            FakeCliInstallationDetector detector = new("3.0.0-beta.5");
            CliSetupApplicationService service = new(detector, nativeCliInstaller);

            CliInstallResult result = await service.EnsureGlobalCliCurrentAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(result.Success, Is.True);
            Assert.That(nativeCliInstaller.InstallCount, Is.EqualTo(1));
            Assert.That(
                nativeCliInstaller.InstalledVersion,
                Is.EqualTo(CliConstants.CLI_RELEASE_TAG_PREFIX + CliConstants.MINIMUM_REQUIRED_CLI_VERSION));
            Assert.That(detector.ForceRefreshCount, Is.EqualTo(2));
        }

        private sealed class FakeCliInstallationDetector : ICliInstallationDetector
        {
            private readonly string _version;

            public FakeCliInstallationDetector(string version)
            {
                _version = version;
            }

            public int ForceRefreshCount { get; private set; }

            public bool IsCliInstalled() => _version != null;
            public string GetCachedCliVersion() => _version;
            public string GetCachedCliExecutablePath() => "";
            public bool IsCheckCompleted() => true;
            public Task RefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;

            public Task ForceRefreshCliVersionAsync(CancellationToken ct)
            {
                ForceRefreshCount++;
                return Task.CompletedTask;
            }

            public void InvalidateCache() { }
        }

        private sealed class FakeNativeCliInstaller : INativeCliInstaller
        {
            public string InstalledVersion { get; private set; }
            public int InstallCount { get; private set; }

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                return false;
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string cliReleaseTag,
                CancellationToken ct)
            {
                InstallCount++;
                InstalledVersion = cliReleaseTag;
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public NativeCliInstallCommand GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string cliReleaseTag,
                bool removeLegacyLaunchers)
            {
                return new NativeCliInstallCommand("sh", "-c true", $"install {cliReleaseTag}");
            }
        }
    }
}
