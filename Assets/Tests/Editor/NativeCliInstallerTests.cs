using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests
{
    public class NativeCliInstallerTests
    {
        [Test]
        public void GetInstallCommand_OnMacUsesDirectInstallScriptWithoutNpm()
        {
            // Verifies that macOS installs through the native release script, not npm.
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.0",
                false);

            Assert.That(command.FileName, Is.EqualTo("/bin/sh"));
            Assert.That(command.Arguments, Does.Contain("https://github.com/hatayama/unity-cli-loop/releases/download/v3.0.0-beta.0/install.sh"));
            Assert.That(command.Arguments, Does.Contain("ULOOP_VERSION='v3.0.0-beta.0'"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsUsesPowerShellInstallScriptWithoutNpm()
        {
            // Verifies that Windows installs through the native release script, not npm.
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.0",
                false);

            Assert.That(command.FileName, Is.EqualTo("powershell"));
            Assert.That(command.Arguments, Does.Contain("https://github.com/hatayama/unity-cli-loop/releases/download/v3.0.0-beta.0/install.ps1"));
            Assert.That(command.Arguments, Does.Contain("$env:ULOOP_VERSION='v3.0.0-beta.0'"));
            Assert.That(command.Arguments, Does.Not.Contain("ULOOP_REMOVE_LEGACY"));
            Assert.That(command.ManualCommand, Does.Contain("irm"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnMacCanOptIntoLegacyNpmRemoval()
        {
            // Verifies that UI-triggered macOS installs can opt into removing the legacy npm launcher.
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.0",
                true);

            Assert.That(command.Arguments, Does.Contain("ULOOP_REMOVE_LEGACY='1'"));
            Assert.That(command.ManualCommand, Does.Contain("ULOOP_REMOVE_LEGACY='1'"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsCanOptIntoLegacyNpmRemoval()
        {
            // Verifies that UI-triggered Windows installs can opt into removing the legacy npm launcher.
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.0",
                true);

            Assert.That(command.Arguments, Does.Contain("$env:ULOOP_REMOVE_LEGACY='1'"));
            Assert.That(command.ManualCommand, Does.Contain("$env:ULOOP_REMOVE_LEGACY='1'"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnWindowsPrependsMissingNativeInstallDir()
        {
            // Verifies that Unity's current Windows PATH prefers the freshly installed native CLI.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "C:\\npm",
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\Users\\masamichi\\Programs\\uloop\\bin;C:\\npm"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnWindowsMovesExistingNativeInstallDirToFront()
        {
            // Verifies that a later Windows native install dir does not leave an earlier npm shim first.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "C:\\npm;C:\\USERS\\MASAMICHI\\PROGRAMS\\ULOOP\\BIN",
                "C:\\Users\\masamichi\\Programs\\uloop\\bin",
                RuntimePlatform.WindowsEditor);

            Assert.That(result, Is.EqualTo("C:\\Users\\masamichi\\Programs\\uloop\\bin;C:\\npm"));
        }

        [Test]
        public void BuildPathWithInstallDirectory_OnMacPrependsMissingNativeInstallDir()
        {
            // Verifies that POSIX PATH prefers the freshly installed native CLI.
            string result = NativeCliInstaller.BuildPathWithInstallDirectory(
                "/usr/local/bin",
                "/Users/masamichi/.local/bin",
                RuntimePlatform.OSXEditor);

            Assert.That(result, Is.EqualTo("/Users/masamichi/.local/bin:/usr/local/bin"));
        }

        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnMacMatchesInstallerDefault()
        {
            // Verifies that Unity mirrors the POSIX installer default install directory.
            string result = NativeCliInstaller.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.OSXEditor,
                "/Users/masamichi",
                null);

            Assert.That(result, Is.EqualTo(System.IO.Path.Combine("/Users/masamichi", ".local", "bin")));
        }

        [Test]
        public void GetDefaultInstallDirectoryFromRoots_OnWindowsMatchesInstallerDefault()
        {
            // Verifies that Unity mirrors the PowerShell installer default install directory.
            string result = NativeCliInstaller.GetDefaultInstallDirectoryFromRoots(
                RuntimePlatform.WindowsEditor,
                null,
                "C:\\Users\\masamichi\\AppData\\Local");

            Assert.That(result, Is.EqualTo(System.IO.Path.Combine(
                "C:\\Users\\masamichi\\AppData\\Local",
                "Programs",
                "uloop",
                "bin")));
        }
    }
}
