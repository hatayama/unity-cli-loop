using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies profile writes for CLI PATH setup.
    /// </summary>
    public class CliPathSetupWriterTests
    {
        [Test]
        public void Apply_WhenProfileHasNoTrailingNewlinePrependsNewlineBeforeAppending()
        {
            // Verifies that appending the PATH line never joins it to the previous shell command.
            CliPathSetupPlan plan = CreateZshPlan();
            List<string> appendedContent = new List<string>();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "# existing",
                path => new DirectoryInfo(path),
                (path, content) => appendedContent.Add(content));

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendedContent, Has.Count.EqualTo(1));
            Assert.That(appendedContent[0], Is.EqualTo("\nexport PATH=\"$HOME/.local/bin:$PATH\"\n"));
        }

        [Test]
        public void Apply_WhenCanonicalLineExistsDoesNotAppend()
        {
            // Verifies that the writer does not duplicate the exact line it owns.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void Apply_WhenLineIsCommentedAppends()
        {
            // Verifies that disabled PATH lines are not treated as configured.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "# export PATH=\"$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_WhenInstallDirectoryAppearsInPathAssignmentDoesNotAppend()
        {
            // Verifies that an existing PATH assignment with the install directory is enough to avoid duplication.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "PATH=\"$PATH:$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        private static CliPathSetupPlan CreateZshPlan()
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
    }
}
