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
                Is.EqualTo(CliConstants.MINIMUM_REQUIRED_CLI_RELEASE_TAG));
        }

        [Test]
        public void GetMinimumRequiredCliVersion_RequiresCompileRecoveryCliRelease()
        {
            // Verifies this package release requires the CLI that polls stored compile status.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller());

            Assert.That(service.GetMinimumRequiredCliVersion(), Is.EqualTo("3.0.0-beta.22"));
        }

        [Test]
        public void GetMinimumRequiredCliReleaseTag_UsesCliGitHubReleaseTag()
        {
            // Verifies installers target the prefixed CLI GitHub Release tag.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller());

            Assert.That(service.GetMinimumRequiredCliReleaseTag(), Is.EqualTo("cli-v3.0.0-beta.22"));
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
                Is.EqualTo("install " + CliConstants.MINIMUM_REQUIRED_CLI_RELEASE_TAG));
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
            public bool IsCliInstalled() => GetCachedCliVersion() != null;
            public string GetCachedCliVersion() => _versions[_versionIndex];
            public string GetCachedCliExecutablePath() => "";
            public bool IsCheckCompleted() => true;
            public Task RefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;
            public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
                => Task.FromResult(true);

            public Task ForceRefreshCliVersionAsync(CancellationToken ct)
            {
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

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                return false;
            }

            public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
            {
                return false;
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string cliReleaseTag,
                CancellationToken ct)
            {
                InstalledVersion = cliReleaseTag;
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(new CliPathSetupPlan(
                    CliPathSetupShellKind.Zsh,
                    "zsh",
                    true,
                    "/Users/ExampleUser/.local/bin",
                    "$HOME/.local/bin",
                    "/Users/ExampleUser/.zshrc",
                    "export PATH=\"$HOME/.local/bin:$PATH\"",
                    "printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"' >> '/Users/ExampleUser/.zshrc'"));
            }

            public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan)
            {
                return new CliPathSetupApplyResult(true, CliPathSetupApplyStatus.Applied, "");
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
