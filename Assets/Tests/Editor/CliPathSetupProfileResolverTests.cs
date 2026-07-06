using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;

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
    }
}
