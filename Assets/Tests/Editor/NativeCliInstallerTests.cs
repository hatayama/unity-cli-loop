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
        [Test]
        public void GetInstallCommand_OnMacKeepsCliOnlyCurlInstallerAvailable()
        {
            // Verifies that editor and CLI installs use the same channel installer script, not npm.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.3",
                false,
                "/bin/zsh");

            Assert.That(command.FileName, Is.EqualTo("/bin/zsh"));
            Assert.That(command.Arguments, Does.Contain("-l -i -c"));
            Assert.That(command.Arguments, Does.Contain("https://raw.githubusercontent.com/hatayama/unity-cli-loop/v3-beta/scripts/install.sh"));
            Assert.That(command.Arguments, Does.Contain($"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c"));
            Assert.That(command.Arguments, Does.Contain("ULOOP_VERSION"));
            Assert.That(command.Arguments, Does.Contain("cli-v3.0.0-beta.3"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnMacDelegatesPosixSnippetThroughSh()
        {
            // Verifies that fish or other login shells only load environment before POSIX script execution.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.3",
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
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.3",
                false,
                "/bin/zsh");

            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Contain(" -o "));
            Assert.That(command.ManualCommand, Does.Contain(" && "));
            Assert.That(command.ManualCommand, Does.Not.Contain("|"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsKeepsCliOnlyPowerShellInstallerAvailable()
        {
            // Verifies that editor and CLI installs use the same channel PowerShell installer script.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.3",
                false,
                "/bin/zsh");

            Assert.That(command.FileName, Is.EqualTo("powershell"));
            Assert.That(command.Arguments, Does.Contain("https://raw.githubusercontent.com/hatayama/unity-cli-loop/v3-beta/scripts/install.ps1"));
            Assert.That(command.Arguments, Does.Contain("$env:ULOOP_VERSION='cli-v3.0.0-beta.3'"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("irm"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnMacCliOnlyInstallerDoesNotAdvertiseWindowsLegacyCleanup()
        {
            // Verifies that macOS manual commands do not expose old cleanup flags.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.3",
                true,
                "/bin/zsh");

            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsDoesNotNeedLegacyCleanupFlag()
        {
            // Verifies that Windows installs rely on the native CLI install command for legacy cleanup.
            NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.3",
                true,
                "/bin/zsh");

            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
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
                NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                    RuntimePlatform.OSXEditor,
                    "3.0.0-beta.3",
                    false,
                    "/bin/zsh",
                    packageResolvedPath);

                Assert.That(command.FileName, Is.EqualTo("/bin/zsh"));
                Assert.That(command.Arguments, Does.Contain("-l -i -c"));
                Assert.That(command.ManualCommand, Does.Contain($"{CliConstants.POSIX_SHELL_EXECUTABLE_PATH} -c"));
                Assert.That(command.ManualCommand, Does.Contain(scriptPath));
                Assert.That(command.ManualCommand, Does.Contain("ULOOP_VERSION"));
                Assert.That(command.ManualCommand, Does.Contain("cli-v3.0.0-beta.3"));
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
                NativeCliInstallCommand command = NativeCliInstaller.BuildInstallCommand(
                    RuntimePlatform.WindowsEditor,
                    "3.0.0-beta.3",
                    false,
                    "/bin/zsh",
                    packageResolvedPath);

                Assert.That(command.FileName, Is.EqualTo("powershell"));
                Assert.That(command.ManualCommand, Does.Contain($"& '{scriptPath}'"));
                Assert.That(command.ManualCommand, Does.Contain("$env:ULOOP_VERSION='cli-v3.0.0-beta.3'"));
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
        public void BuildInstallerScriptUrl_WhenBetaVersionUsesV3BetaInstallerScript()
        {
            // Verifies that beta editor installs call the same beta installer script as CLI commands.
            string url = NativeCliInstaller.BuildInstallerScriptUrl(
                "cli-v3.0.0-beta.3",
                CliConstants.POSIX_INSTALL_SCRIPT_NAME);

            Assert.That(url, Is.EqualTo("https://raw.githubusercontent.com/hatayama/unity-cli-loop/v3-beta/scripts/install.sh"));
        }

        [Test]
        public void BuildInstallerScriptUrl_WhenStableVersionUsesMainInstallerScript()
        {
            // Verifies that stable editor installs call the stable installer script.
            string url = NativeCliInstaller.BuildInstallerScriptUrl(
                "cli-v3.0.0",
                CliConstants.WINDOWS_INSTALL_SCRIPT_NAME);

            Assert.That(url, Is.EqualTo("https://raw.githubusercontent.com/hatayama/unity-cli-loop/main/scripts/install.ps1"));
        }

        [Test]
        public void RunInstallCommand_WhenInstallerExecutableIsMissingReturnsFailure()
        {
            // Verifies that release installer startup failure stays inside the install result contract.
            NativeCliInstallCommand command = new(
                "missing-uloop-release-installer",
                "--version",
                "missing-uloop-release-installer --version");

            CliInstallResult result = NativeCliInstaller.RunInstallCommand(command, CancellationToken.None, 1000);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("Failed to start release CLI installer"));
        }

        [Test]
        public void RunInstallCommand_WhenInstallerDoesNotExitReturnsFailure()
        {
            // Verifies that release installer stalls cannot leave the editor setup task alive forever.
            NativeCliInstallCommand command = BuildLongRunningInstallCommand();

            CliInstallResult result = NativeCliInstaller.RunInstallCommand(command, CancellationToken.None, 50);

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

            CliInstallResult result = NativeCliInstaller.RunUninstallCommand(
                command,
                "/Users/ExampleUser/.local/bin",
                cts.Token,
                1000);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Is.EqualTo("Global CLI uninstall command was canceled."));
        }

        [Test]
        public async Task WaitForUninstallTargetRemovalAsync_WhenDeferredRemovalCompletesReturnsSuccess()
        {
            // Verifies that Settings uninstall waits for deferred launcher self-removal before refreshing CLI status.
            int remainingExistingChecks = 2;
            int delayCount = 0;

            CliInstallResult result = await NativeCliInstaller.WaitForUninstallTargetRemovalAsync(
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin\\uloop.exe",
                CancellationToken.None,
                1000,
                100,
                executablePath => remainingExistingChecks-- > 0,
                (delayMs, ct) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

            Assert.That(result.Success, Is.True, result.ErrorOutput);
            Assert.That(delayCount, Is.EqualTo(2));
        }

        [Test]
        public async Task WaitForUninstallTargetRemovalAsync_WhenTargetRemainsReturnsFailure()
        {
            // Verifies that delayed launcher removal failures do not recache a stale installed CLI.
            int delayCount = 0;

            CliInstallResult result = await NativeCliInstaller.WaitForUninstallTargetRemovalAsync(
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin\\uloop.exe",
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
            Assert.That(result.ErrorOutput, Does.Contain("Timed out waiting for uLoop CLI uninstall"));
            Assert.That(delayCount, Is.EqualTo(3));
        }

        [Test]
        public async Task WaitForUninstallCompletionAsync_OnWindowsWaitsForUserPathRemoval()
        {
            // Verifies that Settings uninstall waits until Windows User PATH no longer resolves the native CLI directory.
            string installDirectory = "C:\\Users\\ExampleUser\\Programs\\uloop\\bin";
            string userPath = installDirectory + ";C:\\npm";
            int delayCount = 0;

            CliInstallResult result = await NativeCliInstaller.WaitForUninstallCompletionAsync(
                installDirectory + "\\uloop.exe",
                installDirectory,
                RuntimePlatform.WindowsEditor,
                CancellationToken.None,
                1000,
                100,
                executablePath => false,
                true,
                (name, target) => userPath,
                (delayMs, ct) =>
                {
                    delayCount++;
                    userPath = "C:\\npm";
                    return Task.CompletedTask;
                });

            Assert.That(result.Success, Is.True, result.ErrorOutput);
            Assert.That(delayCount, Is.EqualTo(1));
        }

        [Test]
        public async Task WaitForUninstallCompletionAsync_OnWindowsFailsWhenUserPathRemains()
        {
            // Verifies that uninstall cannot report success while Windows User PATH still contains the native CLI directory.
            string installDirectory = "C:\\Users\\ExampleUser\\Programs\\uloop\\bin";
            int delayCount = 0;

            CliInstallResult result = await NativeCliInstaller.WaitForUninstallCompletionAsync(
                installDirectory + "\\uloop.exe",
                installDirectory,
                RuntimePlatform.WindowsEditor,
                CancellationToken.None,
                250,
                100,
                executablePath => false,
                true,
                (name, target) => installDirectory + ";C:\\npm",
                (delayMs, ct) =>
                {
                    delayCount++;
                    return Task.CompletedTask;
                });

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("Windows User PATH"));
            Assert.That(result.ErrorOutput, Does.Not.Contain("\\uloop.exe"));
            Assert.That(delayCount, Is.EqualTo(3));
        }

        [Test]
        public async Task WaitForUninstallCompletionAsync_WhenUserPathRemovalIsNotRequiredReportsTargetTimeoutOnly()
        {
            // Verifies fallback uninstall failures do not claim ownership of Windows User PATH cleanup.
            string installDirectory = "C:\\Users\\ExampleUser\\Programs\\uloop\\bin";

            CliInstallResult result = await NativeCliInstaller.WaitForUninstallCompletionAsync(
                installDirectory + "\\uloop.exe",
                installDirectory,
                RuntimePlatform.WindowsEditor,
                CancellationToken.None,
                250,
                100,
                executablePath => true,
                false,
                (name, target) => installDirectory + ";C:\\npm",
                (delayMs, ct) => Task.CompletedTask);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain("\\uloop.exe"));
            Assert.That(result.ErrorOutput, Does.Not.Contain("Windows User PATH"));
        }

        [Test]
        public async Task WaitForUninstallCompletionAsync_WhenUserPathRemovalIsNotRequiredIgnoresUserPath()
        {
            // Verifies fallback launchers can complete uninstall without owning Windows User PATH cleanup.
            string installDirectory = "C:\\Users\\ExampleUser\\Programs\\uloop\\bin";

            CliInstallResult result = await NativeCliInstaller.WaitForUninstallCompletionAsync(
                installDirectory + "\\uloop.exe",
                installDirectory,
                RuntimePlatform.WindowsEditor,
                CancellationToken.None,
                250,
                100,
                executablePath => false,
                false,
                (name, target) => installDirectory + ";C:\\npm",
                (delayMs, ct) => Task.CompletedTask);

            Assert.That(result.Success, Is.True, result.ErrorOutput);
        }

        [Test]
        public void UninstallCompletionTimeout_IsLongEnoughForDeferredWindowsPowerShellCleanup()
        {
            // Verifies Settings uninstall does not report failure before Windows deferred cleanup can finish.
            Assert.That(NativeCliInstaller.UNINSTALL_COMPLETION_TIMEOUT_MS, Is.GreaterThanOrEqualTo(30000));
        }

        [Test]
        public void BuildUninstallCommand_OnMacRunsInstalledLauncher()
        {
            // Verifies that editor uninstall delegates removal to the installed uloop command.
            NativeCliInstallCommand command = NativeCliInstaller.BuildUninstallCommand(
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
            NativeCliInstallCommand command = NativeCliInstaller.BuildUninstallCommand(
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(command.FileName, Does.Contain("C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin"));
            Assert.That(command.FileName, Does.EndWith("uloop.exe"));
            Assert.That(command.Arguments, Is.EqualTo("uninstall"));
            Assert.That(command.ManualCommand, Does.Contain("C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin"));
            Assert.That(command.ManualCommand, Does.EndWith("uloop.exe\" uninstall"));
        }

        [Test]
        public void BuildCurrentPackageUninstallCommand_OnWindowsUsesPackageLauncher()
        {
            // Verifies that Windows editor uninstall uses the package CLI so stale installed launchers cannot own cleanup.
            string tempRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-native-local-cli-tests",
                System.Guid.NewGuid().ToString("N"));
            string packageResolvedPath = Path.Combine(
                tempRoot,
                CliConstants.UNITY_PACKAGES_DIR_NAME,
                CliConstants.PACKAGE_SOURCE_DIR_NAME);
            string cliDirectory = Path.Combine(
                packageResolvedPath,
                CliConstants.CLI_PACKAGE_DIR_NAME,
                CliConstants.DIST_DIR_NAME,
                CliConstants.WINDOWS_AMD64_DIST_DIR_NAME);
            string cliPath = Path.Combine(cliDirectory, CliConstants.GLOBAL_WINDOWS_COMMAND_NAME);

            Directory.CreateDirectory(cliDirectory);
            File.WriteAllText(cliPath, string.Empty);

            try
            {
                NativeCliInstallCommand command = NativeCliInstaller.BuildCurrentPackageUninstallCommand(
                    "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin",
                    RuntimePlatform.WindowsEditor,
                    packageResolvedPath);

                Assert.That(command.FileName, Is.EqualTo(cliPath));
                Assert.That(command.Arguments, Is.EqualTo("uninstall"));
                Assert.That(command.ManualCommand, Is.EqualTo($"\"{cliPath}\" uninstall"));
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
        public void BuildCurrentPackageUninstallCommand_OnWindowsFallsBackToInstalledLauncher()
        {
            // Verifies that package-manager layouts without bundled CLI can still use the installed launcher.
            NativeCliInstallCommand command = NativeCliInstaller.BuildCurrentPackageUninstallCommand(
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor,
                "C:\\missing-package");

            Assert.That(command.FileName, Does.EndWith("uloop.exe"));
            Assert.That(command.FileName, Does.Contain("C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin"));
            Assert.That(command.Arguments, Is.EqualTo("uninstall"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnWindowsPrependsMissingNativeInstallDir()
        {
            // Verifies that Unity's current Windows PATH prefers the freshly installed native CLI.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
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
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "C:\\npm;C:\\USERS\\EXAMPLEUSER\\PROGRAMS\\ULOOP\\BIN",
                "C:\\Users\\ExampleUser\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\Users\\ExampleUser\\Programs\\uloop\\bin;C:\\npm"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnMacPrependsMissingNativeInstallDir()
        {
            // Verifies that POSIX PATH prefers the freshly installed native CLI.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "/usr/local/bin",
                "/Users/ExampleUser/.local/bin",
                RuntimePlatform.OSXEditor);

            Assert.That(result, Is.EqualTo("/Users/ExampleUser/.local/bin:/usr/local/bin"));
        }

        [Test]
        public void BuildPathWithoutInstallDirectory_OnWindowsRemovesNativeInstallDir()
        {
            // Verifies that Windows uninstall removes the native CLI directory from PATH without removing npm.
            string result = NativeCliInstaller.BuildPathWithoutInstallDirectory(
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
            string result = NativeCliInstaller.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.OSXEditor,
                "/Users/ExampleUser",
                null);

            Assert.That(result, Is.EqualTo(System.IO.Path.Combine("/Users/ExampleUser", ".local", "bin")));
        }

        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnWindowsMatchesInstallerDefault()
        {
            // Verifies that Unity mirrors the PowerShell installer default install directory.
            string result = NativeCliInstaller.GetDefaultInstallDirectoryFromRoots(
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
            bool result = NativeCliInstaller.IsPackageOwnedInstallPath(
                "C:/Users/ExampleUser/AppData/Local/Programs/uloop/bin/uloop.exe",
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.True);
        }

        [Test]
        public void IsPackageOwnedInstallPath_WhenExecutableIsSharedCommandReturnsFalse()
        {
            // Verifies that same-version shared commands do not route the settings button to uninstall.
            bool result = NativeCliInstaller.IsPackageOwnedInstallPath(
                "C:\\Tools\\uloop.exe",
                "C:\\Users\\ExampleUser\\AppData\\Local\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.False);
        }

    }
}
