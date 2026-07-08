using System.IO;
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
    /// Test fixture that verifies Native CLI Installer behavior.
    /// </summary>
    public class NativeCliInstallerTests
    {
        private const string TestBetaCliVersion = "3.0.0-beta.3";
        private const string TestBetaReleaseTag = "dispatcher-v3.0.0-beta.3";
        private const string TestStableReleaseTag = "dispatcher-v3.0.0";

        [Test]
        public void GetInstallCommand_OnMacKeepsDispatcherCurlInstallerAvailable()
        {
            // Verifies that editor installs use the dispatcher installer script, not npm.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildRemoteInstallCommand(
                RuntimePlatform.OSXEditor,
                TestBetaCliVersion,
                false,
                "/bin/zsh");

            Assert.That(command.FileName, Is.EqualTo("/bin/zsh"));
            Assert.That(command.Arguments, Does.Contain("-l -i -c"));
            Assert.That(command.Arguments, Does.Contain($"https://raw.githubusercontent.com/hatayama/unity-cli-loop/{TestBetaReleaseTag}/scripts/install.sh"));
            Assert.That(command.Arguments, Does.Contain($"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c"));
            Assert.That(command.Arguments, Does.Contain("ULOOP_VERSION"));
            Assert.That(command.Arguments, Does.Contain(TestBetaReleaseTag));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnMacDelegatesPosixSnippetThroughSh()
        {
            // Verifies that fish or other login shells only load environment before POSIX script execution.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildRemoteInstallCommand(
                RuntimePlatform.OSXEditor,
                TestBetaCliVersion,
                false,
                "/opt/homebrew/bin/fish");

            Assert.That(command.FileName, Is.EqualTo("/opt/homebrew/bin/fish"));
            Assert.That(command.Arguments, Does.Contain("-l -i -c"));
            Assert.That(command.ManualCommand, Does.StartWith($"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c "));
            Assert.That(command.ManualCommand, Does.Contain("tmp_script=$(mktemp)"));
            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
        }

        [Test]
        public void GetInstallCommand_OnMacPropagatesInstallerDownloadFailure()
        {
            // Verifies that editor installs do not report success when curl fails before script execution.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildRemoteInstallCommand(
                RuntimePlatform.OSXEditor,
                TestBetaCliVersion,
                false,
                "/bin/zsh");

            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Contain(" -o "));
            Assert.That(command.ManualCommand, Does.Contain(" && "));
            Assert.That(command.ManualCommand, Does.Not.Contain("|"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsKeepsDispatcherPowerShellInstallerAvailable()
        {
            // Verifies that editor installs use the dispatcher PowerShell installer script.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildRemoteInstallCommand(
                RuntimePlatform.WindowsEditor,
                TestBetaCliVersion,
                false,
                "/bin/zsh");

            Assert.That(command.FileName, Is.EqualTo("powershell"));
            Assert.That(command.Arguments, Does.Contain($"https://raw.githubusercontent.com/hatayama/unity-cli-loop/{TestBetaReleaseTag}/scripts/install.ps1"));
            Assert.That(command.Arguments, Does.Contain($"$env:ULOOP_VERSION='{TestBetaReleaseTag}'"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("irm"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnMacDispatcherInstallerDoesNotAdvertiseWindowsLegacyCleanup()
        {
            // Verifies that macOS manual commands do not expose old cleanup flags.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildRemoteInstallCommand(
                RuntimePlatform.OSXEditor,
                TestBetaCliVersion,
                true,
                "/bin/zsh");

            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsDoesNotNeedLegacyCleanupFlag()
        {
            // Verifies that Windows installs rely on the native CLI install command for legacy cleanup.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildRemoteInstallCommand(
                RuntimePlatform.WindowsEditor,
                TestBetaCliVersion,
                true,
                "/bin/zsh");

            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
        }

        [Test]
        public void GetInstallCommand_WhenVPrefixedVersionUsesDispatcherReleaseTag()
        {
            // Verifies root release tags are normalized to dispatcher releases for installer scripts.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildRemoteInstallCommand(
                RuntimePlatform.OSXEditor,
                "v3.0.0",
                false,
                "/bin/zsh");

            Assert.That(command.Arguments, Does.Contain("dispatcher-v3.0.0"));
            Assert.That(
                command.Arguments,
                Does.Contain("https://raw.githubusercontent.com/hatayama/unity-cli-loop/dispatcher-v3.0.0/scripts/install.sh"));
        }

        [Test]
        public void GetInstallCommand_WhenLocalPackageOnMacUsesPackageLocalInstaller()
        {
            // Verifies that local package development tests exercise the checked-out installer script.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-local-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string packageResolvedPath = Path.Combine(
                tempRoot,
                CliConstants.UNITY_PACKAGES_DIR_NAME,
                CliConstants.PACKAGE_SOURCE_DIR_NAME);
            string scriptsDirectory = Path.Combine(tempRoot, CliConstants.SCRIPTS_DIR_NAME);
            string scriptPath = Path.Combine(scriptsDirectory, CliConstants.POSIX_INSTALL_SCRIPT_NAME);

            Directory.CreateDirectory(packageResolvedPath);
            Directory.CreateDirectory(scriptsDirectory);
            File.WriteAllText(scriptPath, string.Empty);

            try
            {
                NativeCliInstallCommand command = NativeCliCommandBuilder.BuildInstallCommandWithPackagePath(
                    RuntimePlatform.OSXEditor,
                    TestBetaCliVersion,
                    false,
                    "/bin/zsh",
                    packageResolvedPath);

                Assert.That(command.FileName, Is.EqualTo("/bin/zsh"));
                Assert.That(command.Arguments, Does.Contain("-l -i -c"));
                Assert.That(command.ManualCommand, Does.Contain($"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c"));
                Assert.That(command.ManualCommand, Does.Contain(scriptPath));
                Assert.That(command.ManualCommand, Does.Contain("ULOOP_VERSION"));
                Assert.That(command.ManualCommand, Does.Contain(TestBetaReleaseTag));
                Assert.That(command.ManualCommand, Does.Not.Contain("curl -fsSL"));
                Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void GetInstallCommand_WhenLocalPackageOnWindowsUsesPackageLocalInstaller()
        {
            // Verifies that local package development tests use the checked-out PowerShell installer.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-local-installer-tests",
                System.Guid.NewGuid().ToString("N"));
            string packageResolvedPath = Path.Combine(
                tempRoot,
                CliConstants.UNITY_PACKAGES_DIR_NAME,
                CliConstants.PACKAGE_SOURCE_DIR_NAME);
            string scriptsDirectory = Path.Combine(tempRoot, CliConstants.SCRIPTS_DIR_NAME);
            string scriptPath = Path.Combine(scriptsDirectory, CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);

            Directory.CreateDirectory(packageResolvedPath);
            Directory.CreateDirectory(scriptsDirectory);
            File.WriteAllText(scriptPath, string.Empty);

            try
            {
                NativeCliInstallCommand command = NativeCliCommandBuilder.BuildInstallCommandWithPackagePath(
                    RuntimePlatform.WindowsEditor,
                    TestBetaCliVersion,
                    false,
                    "/bin/zsh",
                    packageResolvedPath);

                Assert.That(command.FileName, Is.EqualTo("powershell"));
                Assert.That(command.ManualCommand, Does.Contain($"& '{scriptPath}'"));
                Assert.That(command.ManualCommand, Does.Contain($"$env:ULOOP_VERSION='{TestBetaReleaseTag}'"));
                Assert.That(command.ManualCommand, Does.Not.Contain("irm"));
                Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
            }
            finally
            {
                if (Directory.Exists(tempRoot))
                {
                    Directory.Delete(tempRoot, true);
                }
            }
        }

        [Test]
        public void BuildInstallerScriptUrl_WhenBetaVersionUsesReleaseInstallerScript()
        {
            // Verifies that beta editor installs use the script shipped with the selected dispatcher release.
            string url = NativeCliCommandBuilder.BuildInstallerScriptUrl(
                TestBetaReleaseTag,
                CliConstants.POSIX_INSTALL_SCRIPT_NAME);

            Assert.That(url, Is.EqualTo($"https://raw.githubusercontent.com/hatayama/unity-cli-loop/{TestBetaReleaseTag}/scripts/install.sh"));
        }

        [Test]
        public void BuildInstallerScriptUrl_WhenStableVersionUsesReleaseInstallerScript()
        {
            // Verifies that stable editor installs use the script shipped with the selected dispatcher release.
            string url = NativeCliCommandBuilder.BuildInstallerScriptUrl(
                TestStableReleaseTag,
                CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);

            Assert.That(url, Is.EqualTo($"https://raw.githubusercontent.com/hatayama/unity-cli-loop/{TestStableReleaseTag}/scripts/install.ps1"));
        }

        [Test]
        public void RunInstallCommand_WhenInstallerExecutableIsMissingReturnsFailure()
        {
            // Verifies that release installer startup failure stays inside the install result contract.
            NativeCliInstallCommand command = new(
                "missing-uloop-release-installer",
                "--version",
                "missing-uloop-release-installer --version");

            CliInstallResult result = NativeCliSetupCommandRunner.RunInstallCommand(
                command,
                CancellationToken.None,
                1000);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("Failed to start release CLI installer"));
        }

        [Test]
        public void RunInstallCommand_WhenInstallerDoesNotExitReturnsFailure()
        {
            // Verifies that release installer stalls cannot leave the editor setup task alive forever.
            NativeCliInstallCommand command = BuildLongRunningInstallCommand();

            CliInstallResult result = NativeCliSetupCommandRunner.RunInstallCommand(
                command,
                CancellationToken.None,
                50);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("timed out"));
        }

        [Test]
        public void RunUninstallCommand_WhenCanceledReportsUninstallCommand()
        {
            // Verifies shared setup command cancellation reports the uninstall operation.
            NativeCliInstallCommand command = BuildLongRunningInstallCommand();
            using CancellationTokenSource cts = new();
            cts.CancelAfter(10);

            CliInstallResult result = NativeCliSetupCommandRunner.RunUninstallCommand(
                command,
                "/Users/ExampleUser/.local/bin",
                cts.Token,
                1000);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Is.EqualTo("Global CLI uninstall command was canceled."));
        }

        [Test]
        public async Task WaitForUninstallCompletionAsync_WhenTargetRemainsReportsTimeout()
        {
            // Verifies uninstall completion reports the launcher path when deferred self-removal times out.
            string targetPath = "C:\\Users\\ExampleUser\\Programs\\uloop\\bin\\uloop.exe";
            int delayCount = 0;

            CliInstallResult result = await NativeCliUninstallCompletionWaiter.WaitForUninstallCompletionAsync(
                targetPath,
                CancellationToken.None,
                250,
                100,
                executablePath => true,
                (delayMs, ct) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain(targetPath));
            Assert.That(delayCount, Is.EqualTo(3));
        }

        [Test]
        public async Task WaitForUninstallCompletionAsync_WhenTargetIsRemovedReturnsSuccess()
        {
            // Verifies uninstall completion succeeds as soon as deferred launcher self-removal finishes.

            CliInstallResult result = await NativeCliUninstallCompletionWaiter.WaitForUninstallCompletionAsync(
                "C:\\Users\\ExampleUser\\Programs\\uloop\\bin\\uloop.exe",
                CancellationToken.None,
                250,
                100,
                executablePath => false,
                (delayMs, ct) => Task.CompletedTask);

            Assert.That(result.Success, Is.True, result.ErrorOutput);
        }

        [Test]
        public void UninstallCompletionTimeout_IsLongEnoughForDeferredWindowsPowerShellCleanup()
        {
            // Verifies Settings uninstall does not report failure before Windows deferred cleanup can finish.
            Assert.That(
                NativeCliUninstallCompletionWaiter.UNINSTALL_COMPLETION_TIMEOUT_MS,
                Is.GreaterThanOrEqualTo(30000));
        }

        [Test]
        public void BuildUninstallCommand_OnMacRunsInstalledLauncher()
        {
            // Verifies that editor uninstall delegates removal to the installed uloop command.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildUninstallCommand(
                "/Users/ExampleUser/.local/bin",
                RuntimePlatform.OSXEditor);

            Assert.That(command.FileName, Is.EqualTo("/Users/ExampleUser/.local/bin/uloop"));
            Assert.That(command.Arguments, Is.EqualTo("uninstall"));
            Assert.That(command.ManualCommand, Is.EqualTo("\"/Users/ExampleUser/.local/bin/uloop\" uninstall"));
        }

        [Test]
        public void BuildUninstallCommand_OnWindowsRunsInstalledLauncher()
        {
            // Verifies that Windows editor uninstall delegates removal to the installed uloop command.
            NativeCliInstallCommand command = NativeCliCommandBuilder.BuildUninstallCommand(
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(command.FileName, Does.Contain("C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin"));
            Assert.That(command.FileName, Does.EndWith("uloop.exe"));
            Assert.That(command.Arguments, Is.EqualTo("uninstall"));
            Assert.That(command.ManualCommand, Does.Contain("C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin"));
            Assert.That(command.ManualCommand, Does.EndWith("uloop.exe\" uninstall"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnWindowsPrependsMissingNativeInstallDir()
        {
            // Verifies that Unity's current Windows PATH prefers the freshly installed native CLI.
            string result = NativeCliInstallPathResolver.BuildPathWithInstallDirectory(
                "C:\\npm",
                "C:\\Users\\ExampleUser\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\Users\\ExampleUser\\Programs\\uloop\\bin;C:\\npm"));
        }

        private static NativeCliInstallCommand BuildLongRunningInstallCommand()
        {
            if (UnityEngine.Application.platform == RuntimePlatform.WindowsEditor)
            {
                return new NativeCliInstallCommand(
                    "powershell",
                    "-NoProfile -ExecutionPolicy Bypass -Command \"Start-Sleep -Seconds 5\"",
                    "Start-Sleep -Seconds 5");
            }

            return new NativeCliInstallCommand(
                "/bin/sh",
                "-c \"sleep 5\"",
                "sleep 5");
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnWindowsMovesExistingNativeInstallDirToFront()
        {
            // Verifies that a later Windows native install dir does not leave an earlier npm shim first.
            string result = NativeCliInstallPathResolver.BuildPathWithInstallDirectory(
                "C:\\npm;C:\\USERS\\EXAMPLEUSER\\PROGRAMS\\ULOOP\\BIN",
                "C:\\Users\\ExampleUser\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\Users\\ExampleUser\\Programs\\uloop\\bin;C:\\npm"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnWindowsRemovesTrailingSeparatorDuplicate()
        {
            // Verifies that a trailing separator does not preserve a duplicate native install directory.
            string result = NativeCliInstallPathResolver.BuildPathWithInstallDirectory(
                "C:\\npm;C:\\Users\\ExampleUser\\Programs\\uloop\\bin\\;C:\\Tools",
                "C:\\Users\\ExampleUser\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(
                result,
                Is.EqualTo("C:\\Users\\ExampleUser\\Programs\\uloop\\bin;C:\\npm;C:\\Tools"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnMacPrependsMissingNativeInstallDir()
        {
            // Verifies that POSIX PATH prefers the freshly installed native CLI.
            string result = NativeCliInstallPathResolver.BuildPathWithInstallDirectory(
                "/usr/local/bin",
                "/Users/ExampleUser/.local/bin",
                RuntimePlatform.OSXEditor);

            Assert.That(result, Is.EqualTo("/Users/ExampleUser/.local/bin:/usr/local/bin"));
        }

        [Test]
        public void BuildPathWithoutInstallDirectory_OnWindowsRemovesNativeInstallDir()
        {
            // Verifies that Windows uninstall removes the native CLI directory from PATH without removing npm.
            string result = NativeCliInstallPathResolver.BuildPathWithoutInstallDirectory(
                "C:\\npm;C:\\USERS\\EXAMPLEUSER\\PROGRAMS\\ULOOP\\BIN\\;C:\\Tools",
                "C:\\Users\\ExampleUser\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\npm;C:\\Tools"));
        }

        [Test]
        public void FinishSuccessfulInstall_WhenInstallerSucceededUpdatesCurrentProcessPathOnly()
        {
            // Verifies that User PATH persistence belongs to the native CLI install command, not the editor wrapper.
            bool appliedCurrentPath = false;

            CliInstallResult result = NativeCliInstaller.FinishSuccessfulInstall(
                new CliInstallResult(true, ""),
                "C:\\Users\\ExampleUser\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor,
                platform => { appliedCurrentPath = true; });

            Assert.That(result.Success, Is.True);
            Assert.That(appliedCurrentPath, Is.True);
        }

        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnMacMatchesInstallerDefault()
        {
            // Verifies that Unity mirrors the POSIX installer default install directory.
            string result = NativeCliInstallPathResolver.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.OSXEditor,
                "/Users/ExampleUser",
                null);

            Assert.That(result, Is.EqualTo(System.IO.Path.Combine("/Users/ExampleUser", ".local", "bin")));
        }

        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnWindowsMatchesInstallerDefault()
        {
            // Verifies that Unity mirrors the PowerShell installer default install directory.
            string result = NativeCliInstallPathResolver.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.WindowsEditor,
                null,
                "C:\\Users\\ExampleUser\\AppData\\Local");

            Assert.That(result, Is.EqualTo(System.IO.Path.Combine(
                "C:\\Users\\ExampleUser\\AppData\\Local",
                "Programs",
                "uloop",
                "bin")));
        }

        [Test]
        public void IsPackageOwnedInstallPath_WhenExecutableMatchesInstallDirectoryReturnsTrue()
        {
            // Verifies that uninstall is available only for the package-owned command path.
            bool result = NativeCliInstallPathResolver.IsPackageOwnedInstallPath(
                "C:/Users/ExampleUser/AppData/Local/Programs/uloop/bin/uloop.exe",
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsPackageOwnedInstallPath_WhenExecutableIsSharedCommandReturnsFalse()
        {
            // Verifies that same-version shared commands do not route the settings button to uninstall.
            bool result = NativeCliInstallPathResolver.IsPackageOwnedInstallPath(
                "C:\\Tools\\uloop.exe",
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.False);
        }

    }
}
