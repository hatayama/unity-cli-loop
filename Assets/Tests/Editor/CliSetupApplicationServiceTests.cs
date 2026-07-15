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
        public async Task InstallGlobalCliAsync_UsesPinnedDispatcherReleaseTag()
        {
            // Verifies installation uses the immutable dispatcher release tag stamped in the package pin.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller,
                new CliPinReaderService());

            await service.InstallGlobalCliAsync(RuntimePlatform.OSXEditor, CancellationToken.None);

            Assert.That(
                nativeCliInstaller.InstalledVersion,
                Is.EqualTo("dispatcher-v3.0.1-beta.6"));
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
        public void GetGlobalCliInstallCommand_UsesPinnedDispatcherReleaseTag()
        {
            // Verifies fallback manual commands use the immutable dispatcher tag from the bootstrap pin.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller,
                new CliPinReaderService());

            NativeCliInstallCommandLoadResult commandResult = service.GetGlobalCliInstallCommand(
                RuntimePlatform.OSXEditor,
                false);

            Assert.That(commandResult.Success, Is.True, commandResult.ErrorOutput);
            Assert.That(
                commandResult.Command.ManualCommand,
                Is.EqualTo("install dispatcher-v3.0.1-beta.6"));
        }

        [Test]
        public async Task InstallGlobalCliAsync_WhenBootstrapPinIsUnavailableFailsWithoutInvokingInstaller()
        {
            // Verifies a missing bootstrap pin cannot fall back to a derived or latest dispatcher release.
            FakeNativeCliInstaller nativeCliInstaller = new();
            CliSetupApplicationService service = new(
                new FakeCliInstallationDetector(new string[] { null }),
                nativeCliInstaller,
                new FailingBootstrapPinReader());

            CliInstallResult result = await service.InstallGlobalCliAsync(RuntimePlatform.OSXEditor, CancellationToken.None);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("bootstrap pin"));
            Assert.That(nativeCliInstaller.InstalledVersion, Is.Null);
        }

        private static string ExpectedMinimumDispatcherVersion()
        {
            CliPinLoadResult result = new CliPinReaderService().LoadPackagePin();
            Assert.That(result.Success, Is.True, result.ErrorMessage);
            return result.Pin.MinimumDispatcherVersion;
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

        private sealed class FailingBootstrapPinReader : ICliPinReader
        {
            public CliPinLoadResult LoadPackagePin()
            {
                return CliPinLoadResult.FromFailure("bootstrap pin missing");
            }

            public DispatcherBootstrapPinLoadResult LoadDispatcherBootstrapPin()
            {
                return DispatcherBootstrapPinLoadResult.FromFailure("bootstrap pin missing");
            }

            public string LoadMinimumDispatcherVersionOrThrow()
            {
                return "3.0.1";
            }
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
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                CancellationToken ct)
            {
                InstalledVersion = dispatcherReleaseTag;
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

            public NativeCliInstallCommandLoadResult GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string dispatcherReleaseTag,
                string dispatcherArchiveManifest,
                bool removeLegacyLaunchers)
            {
                return NativeCliInstallCommandLoadResult.FromSuccess(
                    new NativeCliInstallCommand("sh", "-c true", $"install {dispatcherReleaseTag}"));
            }
        }
    }
}
