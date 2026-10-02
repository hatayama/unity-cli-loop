using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Domain;
using InfrastructureCliPathSetupProfileResolver = io.github.hatayama.UnityCliLoop.Infrastructure.CliPathSetupProfileResolver;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies shell profile selection for CLI PATH setup.
    /// </summary>
    public class CliPathSetupProfileResolverTests
    {
        [Test]
        public void ResolvePlan_WhenZshUsesZshrcInZdotdir()
        {
            // Verifies that zsh setup honors the explicit ZDOTDIR environment root without probing login hooks.
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/zsh",
                "/Users/ExampleUser",
                "/Users/ExampleUser/.config/zsh",
                null,
                "/Users/ExampleUser/.local/bin",
                path => false);

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Zsh));
            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.config/zsh/.zshrc"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"$HOME/.local/bin:$PATH\""));
        }

        [Test]
        public void ResolvePlan_WhenBashProfileExistsUsesExistingProfile()
        {
            // Verifies that bash setup avoids creating .bash_profile when a login profile already exists.
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                null,
                "/Users/ExampleUser/.local/bin",
                path => path == "/Users/ExampleUser/.profile");

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Bash));
            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.profile"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"$HOME/.local/bin:$PATH\""));
        }

        [Test]
        public void ResolvePlan_WhenFishUsesXdgConfigHome()
        {
            // Verifies that fish setup writes config.fish under XDG_CONFIG_HOME.
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/opt/homebrew/bin/fish",
                "/Users/ExampleUser",
                null,
                "/Users/ExampleUser/Library/Application Support",
                "/Users/ExampleUser/.local/bin",
                path => false);

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Fish));
            Assert.That(
                plan.ConfigurationFilePath,
                Is.EqualTo("/Users/ExampleUser/Library/Application Support/fish/config.fish"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("fish_add_path --move \"$HOME/.local/bin\""));
        }

        [Test]
        public void ResolvePlan_WhenUnsupportedShellDisablesAutomaticApply()
        {
            // Verifies that unknown shells do not expose a command written for a different shell syntax.
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/tcsh",
                "/Users/ExampleUser",
                null,
                null,
                "/Users/ExampleUser/.local/bin",
                path => false);

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Unsupported));
            Assert.That(plan.CanApplyAutomatically, Is.False);
            Assert.That(plan.ManualCommand, Is.Empty);
        }

        [Test]
        public void ResolvePlan_WhenInstallDirectoryIsMissingDoesNotUseExecutableNameAsDirectory()
        {
            // Verifies that missing install roots do not produce misleading PATH directory guidance.
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                null,
                "",
                path => false);

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Unsupported));
            Assert.That(plan.CanApplyAutomatically, Is.False);
            Assert.That(plan.InstallDirectory, Is.Empty);
            Assert.That(plan.ProfileInstallDirectory, Is.Empty);
            Assert.That(plan.ManualCommand, Is.Empty);
        }

        [Test]
        public void ResolvePlan_WhenPlatformIsWindowsDisablesAutomaticApply()
        {
            // Verifies the domain resolver keeps Windows unsupported without shell-specific profile guidance.
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Windows,
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                null,
                "/Users/ExampleUser/.local/bin",
                path => false);

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Unsupported));
            Assert.That(plan.ShellName, Is.EqualTo("windows"));
            Assert.That(plan.CanApplyAutomatically, Is.False);
            Assert.That(plan.ManualCommand, Is.Empty);
        }

        [Test]
        public void ResolvePlan_WhenAdapterReceivesWindowsPlatformMapsToUnsupportedPlan()
        {
            // Verifies that the Unity RuntimePlatform adapter maps WindowsEditor to the Domain Windows policy.
            CliPathSetupPlan plan = InfrastructureCliPathSetupProfileResolver.ResolvePlan(
                RuntimePlatform.WindowsEditor,
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                null,
                "/Users/ExampleUser/.local/bin",
                path => false);

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Unsupported));
            Assert.That(plan.ShellName, Is.EqualTo("windows"));
            Assert.That(plan.CanApplyAutomatically, Is.False);
            Assert.That(plan.ManualCommand, Is.Empty);
        }

        [Test]
        public void ResolvePlan_WhenAdapterReceivesMacPlatformMapsToPosixZshPlan()
        {
            // Verifies that the Unity RuntimePlatform adapter maps macOS editor platforms to the Domain POSIX policy.
            CliPathSetupPlan plan = InfrastructureCliPathSetupProfileResolver.ResolvePlan(
                RuntimePlatform.OSXEditor,
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                null,
                "/Users/ExampleUser/.local/bin",
                path => false);

            Assert.That(plan.ShellKind, Is.EqualTo(CliPathSetupShellKind.Zsh));
            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.zshrc"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"$HOME/.local/bin:$PATH\""));
        }

        /// <summary>
        /// Verifies that bash setup prefers an existing .bash_profile over other login profiles.
        /// </summary>
        [Test]
        public void ResolvePlan_WhenBashProfileAndProfileExist_UsesBashProfile()
        {
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                null,
                "/Users/ExampleUser/.local/bin",
                path => path == "/Users/ExampleUser/.bash_profile" || path == "/Users/ExampleUser/.profile");

            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.bash_profile"));
        }

        /// <summary>
        /// Verifies that bash setup uses an existing .bash_login before falling back to .profile.
        /// </summary>
        [Test]
        public void ResolvePlan_WhenBashLoginAndProfileExist_UsesBashLogin()
        {
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/bash",
                "/Users/ExampleUser",
                null,
                null,
                "/Users/ExampleUser/.local/bin",
                path => path == "/Users/ExampleUser/.bash_login" || path == "/Users/ExampleUser/.profile");

            Assert.That(plan.ConfigurationFilePath, Is.EqualTo("/Users/ExampleUser/.bash_login"));
        }

        /// <summary>
        /// Verifies that the PATH line keeps the literal install directory when no home directory is known.
        /// </summary>
        [Test]
        public void ResolvePlan_WhenHomeDirectoryIsMissing_KeepsLiteralInstallDirectory()
        {
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/zsh",
                null,
                "/opt/zsh-config",
                null,
                "/opt/uloop/bin",
                path => false);

            Assert.That(plan.ProfileInstallDirectory, Is.EqualTo("/opt/uloop/bin"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"/opt/uloop/bin:$PATH\""));
        }

        /// <summary>
        /// Verifies that an install directory equal to the home directory is written as $HOME.
        /// </summary>
        [Test]
        public void ResolvePlan_WhenInstallDirectoryIsHomeDirectory_WritesHomeReference()
        {
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/zsh",
                "/Users/ExampleUser/",
                null,
                null,
                "/Users/ExampleUser",
                path => false);

            Assert.That(plan.ProfileInstallDirectory, Is.EqualTo("$HOME"));
            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"$HOME:$PATH\""));
        }

        /// <summary>
        /// Verifies that characters special inside POSIX double quotes are escaped in the PATH line.
        /// </summary>
        [Test]
        public void ResolvePlan_WhenInstallDirectoryHasShellSpecialCharacters_EscapesThemForPosixShell()
        {
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/bin/zsh",
                "/Users/ExampleUser",
                null,
                null,
                "/opt/a\"b$c`d\\e",
                path => false);

            Assert.That(plan.ConfigurationLine, Is.EqualTo("export PATH=\"/opt/a\\\"b\\$c\\`d\\\\e:$PATH\""));
        }

        /// <summary>
        /// Verifies that fish setup escapes quotes and dollars but leaves backticks, which fish does not expand.
        /// </summary>
        [Test]
        public void ResolvePlan_WhenFishInstallDirectoryHasBacktick_DoesNotEscapeBacktick()
        {
            CliPathSetupPlan plan = CliPathSetupProfileResolver.ResolvePlan(
                CliPathSetupPlatform.Posix,
                "/usr/bin/fish",
                "/Users/ExampleUser",
                null,
                null,
                "/opt/a`b$c",
                path => false);

            Assert.That(plan.ConfigurationLine, Is.EqualTo("fish_add_path --move \"/opt/a`b\\$c\""));
        }
    }
}
