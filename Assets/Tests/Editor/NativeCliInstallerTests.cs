using NUnit.Framework;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests
{
    public class NativeCliInstallerTests
    {
        [Test]
        public void GetInstallCommand_OnMacUsesDirectInstallScriptWithoutNpm()
        {
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.OSXEditor,
                "3.0.0-beta.0");

            Assert.That(command.FileName, Is.EqualTo("/bin/sh"));
            Assert.That(command.Arguments, Does.Contain("https://github.com/hatayama/unity-cli-loop/releases/download/v3.0.0-beta.0/install.sh"));
            Assert.That(command.Arguments, Does.Contain("ULOOP_VERSION='v3.0.0-beta.0'"));
            Assert.That(command.ManualCommand, Does.Contain("curl -fsSL"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }

        [Test]
        public void GetInstallCommand_OnWindowsUsesPowerShellInstallScriptWithoutNpm()
        {
            NativeCliInstallCommand command = NativeCliInstaller.GetInstallCommand(
                RuntimePlatform.WindowsEditor,
                "3.0.0-beta.0");

            Assert.That(command.FileName, Is.EqualTo("powershell"));
            Assert.That(command.Arguments, Does.Contain("https://github.com/hatayama/unity-cli-loop/releases/download/v3.0.0-beta.0/install.ps1"));
            Assert.That(command.Arguments, Does.Contain("$env:ULOOP_VERSION='v3.0.0-beta.0'"));
            Assert.That(command.ManualCommand, Does.Contain("irm"));
            Assert.That(command.ManualCommand, Does.Not.Contain("npm"));
        }
    }
}
