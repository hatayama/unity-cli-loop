using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI setup application service behavior.
    /// </summary>
    public class CliSetupApplicationServiceTests
    {
        [Test]
        public async Task InstallGlobalCliAsync_UsesMinimumRequiredDispatcherReleaseTag()
        {
            // Verifies that manual installs target the dispatcher release derived from the package pin.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller,
                new CliPinReaderService());

            await service.InstallGlobalCliAsync(RuntimePlatform.OSXEditor, CancellationToken.None);

            Assert.That(
                nativeCliInstaller.InstalledVersion,
                Is.EqualTo(ExpectedDispatcherReleaseTag()));
        }

        [Test]
        public void GetMinimumRequiredCliVersion_UsesDispatcherVersion()
        {
            // Verifies setup reads the minimum dispatcher version from the package pin JSON.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller(),
                new CliPinReaderService());

            Assert.That(service.GetMinimumRequiredCliVersion(), Is.EqualTo(ExpectedMinimumDispatcherVersion()));
        }

        [Test]
        public void GetMinimumRequiredCliReleaseTag_UsesDispatcherReleaseTag()
        {
            // Verifies setup derives the prefixed release tag from the package pin instead of a duplicated constant.
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                new FakeNativeCliInstaller(),
                new CliPinReaderService());

            Assert.That(service.GetMinimumRequiredCliReleaseTag(), Is.EqualTo(ExpectedDispatcherReleaseTag()));
        }

        [Test]
        public void GetGlobalCliInstallCommand_UsesMinimumRequiredDispatcherReleaseTag()
        {
            // Verifies that fallback manual commands point at the pin-derived dispatcher release tag.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller,
                new CliPinReaderService());

            NativeCliInstallCommand command = service.GetGlobalCliInstallCommand(
                RuntimePlatform.OSXEditor,
                false);

            Assert.That(
                command.ManualCommand,
                Is.EqualTo("install " + ExpectedDispatcherReleaseTag()));
        }

        private static string ExpectedMinimumDispatcherVersion()
        {
            CliPinLoadResult result = new CliPinReaderService().LoadPackagePin();
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result.Pin.MinimumDispatcherVersion;
        }

        private static string ExpectedDispatcherReleaseTag()
        {
            return CliPinReader.BuildDispatcherReleaseTag(ExpectedMinimumDispatcherVersion());
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
            public bool GetCachedCliIsDispatcher() => false;
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
