using System.Threading;
using System.Threading.Tasks;

using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies the application-owned CLI PATH setup flow.
    /// </summary>
    public class CliPathSetupFlowTests
    {
        [Test]
        public async Task EnsureCliVisibleFromShellAsync_WhenAlreadyVisibleDoesNotApplyProfile()
        {
            // Verifies that fresh shell visibility is the authority and no profile write happens when uloop is visible.
            FakeCliInstallationDetector detector = new(true);
            FakeNativeCliInstaller installer = new FakeNativeCliInstaller(
                CreateSupportedPlan(),
                CreateAppliedResult());
            CliSetupApplicationService service = new(detector, installer);

            CliPathSetupFlowResult result = await service.EnsureCliVisibleFromShellAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupFlowStatus.AlreadyVisible));
            Assert.That(installer.ApplyCount, Is.EqualTo(0));
        }

        [Test]
        public async Task EnsureCliVisibleFromShellAsync_WhenApplySucceedsRechecksShell()
        {
            // Verifies that PATH setup is only considered complete after a second fresh shell check passes.
            FakeCliInstallationDetector detector = new(false, true);
            FakeNativeCliInstaller installer = new FakeNativeCliInstaller(
                CreateSupportedPlan(),
                CreateAppliedResult());
            CliSetupApplicationService service = new(detector, installer);

            CliPathSetupFlowResult result = await service.EnsureCliVisibleFromShellAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupFlowStatus.AppliedAndVisible));
            Assert.That(installer.ApplyCount, Is.EqualTo(1));
            Assert.That(detector.VisibilityCheckCount, Is.EqualTo(2));
        }

        [Test]
        public async Task EnsureCliVisibleFromShellAsync_WhenUnsupportedShellReturnsManualSetup()
        {
            // Verifies that unsupported shells never receive automatic profile edits.
            FakeCliInstallationDetector detector = new(false);
            FakeNativeCliInstaller installer = new FakeNativeCliInstaller(
                CreateUnsupportedPlan(),
                CreateAppliedResult());
            CliSetupApplicationService service = new(detector, installer);

            CliPathSetupFlowResult result = await service.EnsureCliVisibleFromShellAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupFlowStatus.ManualSetupRequired));
            Assert.That(installer.ApplyCount, Is.EqualTo(0));
        }

        [Test]
        public async Task EnsureCliVisibleFromShellAsync_WhenProfileApplyFailsReturnsFailure()
        {
            // Verifies that profile write failures become user-facing PATH setup failures.
            FakeCliInstallationDetector detector = new(false);
            CliPathSetupApplyResult applyResult = new CliPathSetupApplyResult(
                false,
                CliPathSetupApplyStatus.Failed,
                "profile is read-only");
            FakeNativeCliInstaller installer = new FakeNativeCliInstaller(CreateSupportedPlan(), applyResult);
            CliSetupApplicationService service = new CliSetupApplicationService(detector, installer);

            CliPathSetupFlowResult result = await service.EnsureCliVisibleFromShellAsync(
                RuntimePlatform.OSXEditor,
                CancellationToken.None);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupFlowStatus.Failed));
            Assert.That(result.ErrorOutput, Does.Contain("profile is read-only"));
            Assert.That(installer.ApplyCount, Is.EqualTo(1));
            Assert.That(detector.VisibilityCheckCount, Is.EqualTo(1));
        }

        private static CliPathSetupPlan CreateUnsupportedPlan()
        {
            return new CliPathSetupPlan(
                CliPathSetupShellKind.Unsupported,
                "tcsh",
                false,
                "/Users/ExampleUser/.local/bin",
                "/Users/ExampleUser/.local/bin",
                "",
                "",
                "");
        }

        private static CliPathSetupPlan CreateSupportedPlan()
        {
            return new CliPathSetupPlan(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                "/Users/ExampleUser/.local/bin",
                "$HOME/.local/bin",
                "/Users/ExampleUser/.zshrc",
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "printf '\\n%s\\n' 'export PATH=\"$HOME/.local/bin:$PATH\"' >> '/Users/ExampleUser/.zshrc'");
        }

        private static CliPathSetupApplyResult CreateAppliedResult()
        {
            return new CliPathSetupApplyResult(true, CliPathSetupApplyStatus.Applied, "");
        }

        private sealed class FakeCliInstallationDetector : ICliInstallationDetector
        {
            private readonly bool[] _visibilityResults;

            public FakeCliInstallationDetector(params bool[] visibilityResults)
            {
                _visibilityResults = visibilityResults;
            }

            public int VisibilityCheckCount { get; private set; }

            public bool IsCliInstalled() => true;
            public string GetCachedCliVersion() => "3.0.0-beta.14";
            public string GetCachedCliExecutablePath() => "/Users/ExampleUser/.local/bin/uloop";
            public bool IsCheckCompleted() => true;
            public Task RefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;
            public Task ForceRefreshCliVersionAsync(CancellationToken ct) => Task.CompletedTask;
            public void InvalidateCache() { }

            public Task<bool> IsCliVisibleFromShellAsync(RuntimePlatform platform, CancellationToken ct)
            {
                bool result = _visibilityResults[
                    System.Math.Min(VisibilityCheckCount, _visibilityResults.Length - 1)];
                VisibilityCheckCount++;
                return Task.FromResult(result);
            }
        }

        private sealed class FakeNativeCliInstaller : INativeCliInstaller
        {
            private readonly CliPathSetupPlan _plan;
            private readonly CliPathSetupApplyResult _applyResult;

            public FakeNativeCliInstaller(CliPathSetupPlan plan, CliPathSetupApplyResult applyResult)
            {
                _plan = plan;
                _applyResult = applyResult;
            }

            public int ApplyCount { get; private set; }

            public bool IsPackageOwnedCurrentUserInstallPath(string cliExecutablePath, RuntimePlatform platform)
            {
                return true;
            }

            public bool HasPackageOwnedCurrentUserInstall(RuntimePlatform platform)
            {
                return true;
            }

            public Task<CliInstallResult> InstallGlobalCliAsync(
                RuntimePlatform platform,
                string cliReleaseTag,
                CancellationToken ct)
            {
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliInstallResult> UninstallGlobalCliAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(new CliInstallResult(true, ""));
            }

            public Task<CliPathSetupPlan> GetGlobalCliPathSetupPlanAsync(RuntimePlatform platform, CancellationToken ct)
            {
                return Task.FromResult(_plan);
            }

            public CliPathSetupApplyResult ApplyGlobalCliPathSetup(CliPathSetupPlan plan)
            {
                ApplyCount++;
                return _applyResult;
            }

            public NativeCliInstallCommand GetGlobalCliInstallCommand(
                RuntimePlatform platform,
                string cliReleaseTag,
                bool removeLegacyLaunchers)
            {
                return new NativeCliInstallCommand("sh", "-c true", "install");
            }
        }
    }
}
