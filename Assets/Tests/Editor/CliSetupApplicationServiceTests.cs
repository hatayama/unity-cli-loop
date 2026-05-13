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
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller);

            await service.InstallGlobalCliAsync(RuntimePlatform.OSXEditor, CancellationToken.None);

            Assert.That(
                nativeCliInstaller.InstalledVersion,
                Is.EqualTo(CliConstants.CLI_RELEASE_TAG_PREFIX + CliConstants.MINIMUM_REQUIRED_CLI_VERSION));
        }

        [Test]
        public void GetMinimumRequiredCliVersion_RequiresSingleBinaryCliRelease()
        {
            // Verifies this package release rejects CLIs older than the single-binary release.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller());

            Assert.That(service.GetMinimumRequiredCliVersion(), Is.EqualTo("3.0.0-beta.7"));
        }

        [Test]
        public void GetGlobalCliInstallCommand_UsesMinimumRequiredCliReleaseTag()
        {
            // Verifies that fallback manual commands point at the independent CLI release stream.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
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
            FakeCliInstallationDetector detector = new(
                new string[]
                {
                    CliConstants.MINIMUM_REQUIRED_CLI_VERSION,
                    CliConstants.MINIMUM_REQUIRED_CLI_VERSION
                });
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
            FakeCliInstallationDetector detector = new(
                new string[]
                {
                    "3.0.0-beta.5",
                    "3.0.0-beta.5",
                    CliConstants.MINIMUM_REQUIRED_CLI_VERSION
                });
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

        [Test]
        public async Task EnsureGlobalCliCurrentAsync_WhenPostInstallVersionIsStillTooOld_Fails()
        {
            // Verifies that startup reports failure when install succeeds but the detected global CLI is still stale.
            FakeNativeCliInstaller nativeCliInstaller = new();
            FakeCliInstallationDetector detector = new(
                new string[]
                {
                    "3.0.0-beta.5",
                    "3.0.0-beta.5",
                    "3.0.0-beta.5"
                });
            CliSetupApplicationService service = new(detector, nativeCliInstaller);

            CliInstallResult result = await service.EnsureGlobalCliCurrentAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("does not satisfy the package minimum"));
            Assert.That(nativeCliInstaller.InstallCount, Is.EqualTo(1));
            Assert.That(detector.ForceRefreshCount, Is.EqualTo(2));
        }

        [Test]
        public async Task EnsureGlobalCliCurrentAsync_AfterManualUninstall_SkipsInstallForCurrentSession()
        {
            // Verifies that startup auto install does not immediately undo a successful manual uninstall.
            FakeNativeCliInstaller nativeCliInstaller = new();
            FakeCliInstallationDetector detector = new(new string[] { null });
            CliSetupApplicationService service = new(detector, nativeCliInstaller);

            CliInstallResult uninstallResult = await service.UninstallGlobalCliAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);
            CliInstallResult ensureResult = await service.EnsureGlobalCliCurrentAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(uninstallResult.Success, Is.True);
            Assert.That(ensureResult.Success, Is.True);
            Assert.That(nativeCliInstaller.UninstallCount, Is.EqualTo(1));
            Assert.That(nativeCliInstaller.InstallCount, Is.EqualTo(0));
            Assert.That(detector.ForceRefreshCount, Is.EqualTo(0));
        }

        [Test]
        public async Task EnsureGlobalCliCurrentAsync_AfterFailedManualUninstall_StillInstallsMinimumRelease()
        {
            // Verifies that failed manual uninstall attempts do not disable startup recovery.
            FakeNativeCliInstaller nativeCliInstaller = new()
            {
                UninstallResult = new CliInstallResult(false, "uninstall failed")
            };
            FakeCliInstallationDetector detector = new(
                new string[]
                {
                    "3.0.0-beta.5",
                    "3.0.0-beta.5",
                    CliConstants.MINIMUM_REQUIRED_CLI_VERSION
                });
            CliSetupApplicationService service = new(detector, nativeCliInstaller);

            CliInstallResult uninstallResult = await service.UninstallGlobalCliAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);
            CliInstallResult ensureResult = await service.EnsureGlobalCliCurrentAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(uninstallResult.Success, Is.False);
            Assert.That(ensureResult.Success, Is.True);
            Assert.That(nativeCliInstaller.UninstallCount, Is.EqualTo(1));
            Assert.That(nativeCliInstaller.InstallCount, Is.EqualTo(1));
            Assert.That(detector.ForceRefreshCount, Is.EqualTo(2));
        }

        private sealed class FakeCliInstallationDetector : ICliInstallationDetector
        {
            private readonly string[] _versions;
            private int _versionIndex;

            public FakeCliInstallationDetector(string[] versions)
            {
                Debug.Assert(versions != null, "versions must not be null");
                Debug.Assert(versions.Length > 0, "versions must not be empty");

                _versions = versions;
            }

            public int ForceRefreshCount { get; private set; }

            public bool IsCliInstalled() => GetCachedCliVersion() != null;
            public string GetCachedCliVersion() => _versions[_versionIndex];
            public string GetCachedCliExecutablePath() => "";
            public bool IsCheckCompleted() => true;
            public Task RefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;

            public Task ForceRefreshCliVersionAsync(CancellationToken ct)
            {
                ForceRefreshCount++;
                if (_versionIndex < _versions.Length - 1)
                {
                    _versionIndex++;
                }

                return Task.CompletedTask;
            }

            public void InvalidateCache() { }
        }

        private sealed class FakeNativeCliInstaller : INativeCliInstaller
        {
            public string InstalledVersion { get; private set; }
            public int InstallCount { get; private set; }
            public int UninstallCount { get; private set; }
            public CliInstallResult UninstallResult { get; set; } = new(true, "");

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
                UninstallCount++;
                return Task.FromResult(UninstallResult);
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
