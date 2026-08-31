using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Domain;
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
        public void Apply_WhenCanonicalLineIsFollowedByShadowingPathPrependerAppends()
        {
            // Verifies that stale earlier setup lines do not block a repair append at the end of the profile.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.local/bin:$PATH\"\nexport PATH=\"/usr/local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_WhenCanonicalLineIsFollowedByUnrelatedCommandDoesNotAppend()
        {
            // Verifies that unrelated later profile commands do not duplicate an effective PATH setup.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.local/bin:$PATH\"\nalias ll='ls -la'\n",
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
        public void Apply_WhenInstallDirectoryAppearsAfterExistingPathAppends()
        {
            // Verifies that appended install directories do not block a repair that needs to outrank old shims.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "PATH=\"$PATH:$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_WhenInstallDirectoryIsNotFirstPathEntryAppends()
        {
            // Verifies that earlier PATH entries keep shadowing risk even when the install directory is before $PATH.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "export PATH=\"/opt/old:$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_WhenInstallDirectoryAppearsBeforeExistingPathDoesNotAppend()
        {
            // Verifies that profile entries only count as configured when they put the install directory first.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "PATH=\"$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void Apply_WhenSiblingDirectoryAppearsInPathAssignmentAppends()
        {
            // Verifies that similar directory names are not treated as the install directory.
            CliPathSetupPlan plan = CreateZshPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "PATH=\"$PATH:$HOME/.local/bin-old\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_WhenFishProfileSetsUnrelatedPathVariableAppends()
        {
            // Verifies that unrelated fish variables containing PATH do not count as shell PATH setup.
            CliPathSetupPlan plan = CreateFishPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "set -gx GOPATH \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_WhenFishProfileSetsPathVariableDoesNotAppend()
        {
            // Verifies that fish PATH variable setup with the install directory prevents duplication.
            CliPathSetupPlan plan = CreateFishPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "set -gx fish_user_paths \"$HOME/.local/bin\" $fish_user_paths\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        [Test]
        public void Apply_WhenFishProfileAppendsInstallDirectoryAppends()
        {
            // Verifies that fish append-style setup does not block a repair that must prepend the install directory.
            CliPathSetupPlan plan = CreateFishPlan();
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "set -gx fish_user_paths $fish_user_paths \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        [Test]
        public void Apply_WhenProfileWriteFailsReturnsFailedResult()
        {
            // Verifies that a denied shell profile write falls back to the manual setup result path.
            CliPathSetupPlan plan = CreateZshPlan();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => string.Empty,
                path => new DirectoryInfo(path),
                (path, content) => throw new UnauthorizedAccessException("profile is read-only"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Failed));
            Assert.That(result.ErrorOutput, Does.Contain("profile is read-only"));
        }

        [Test]
        public void Apply_WhenProfileReadFailsReturnsFailedResult()
        {
            // Verifies that a denied shell profile read falls back to the manual setup result path.
            CliPathSetupPlan plan = CreateZshPlan();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => throw new IOException("profile cannot be read"),
                path => new DirectoryInfo(path),
                (path, content) => { });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Failed));
            Assert.That(result.ErrorOutput, Does.Contain("profile cannot be read"));
        }

        [Test]
        public void Apply_WhenProfileDirectoryCreationFailsReturnsFailedResult()
        {
            // Verifies that denied profile directory creation falls back to the manual setup result path.
            CliPathSetupPlan plan = CreateZshPlan();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => false,
                path => string.Empty,
                path => throw new IOException("profile directory cannot be created"),
                (path, content) => { });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Failed));
            Assert.That(result.ErrorOutput, Does.Contain("profile directory cannot be created"));
        }

        [Test]
        public void Apply_WhenProfilePathIsInvalidReturnsFailedResult()
        {
            // Verifies that invalid profile paths fall back to the manual setup result path.
            CliPathSetupPlan plan = CreateZshPlan();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => throw new ArgumentException("profile path is invalid"),
                path => string.Empty,
                path => new DirectoryInfo(path),
                (path, content) => { });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Failed));
            Assert.That(result.ErrorOutput, Does.Contain("profile path is invalid"));
        }

        [Test]
        public void Apply_WhenProfilePathIsNotSupportedReturnsFailedResult()
        {
            // Verifies that unsupported profile path formats fall back to the manual setup result path.
            CliPathSetupPlan plan = CreateZshPlan();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => throw new NotSupportedException("profile path is not supported"),
                path => string.Empty,
                path => new DirectoryInfo(path),
                (path, content) => { });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Failed));
            Assert.That(result.ErrorOutput, Does.Contain("profile path is not supported"));
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

        private static CliPathSetupPlan CreateFishPlan()
        {
            return new CliPathSetupPlan(
                CliPathSetupShellKind.Fish,
                "fish",
                true,
                "/Users/ExampleUser/.local/bin",
                "$HOME/.local/bin",
                "/Users/ExampleUser/.config/fish/config.fish",
                "fish_add_path --move \"$HOME/.local/bin\"",
                "mkdir -p '/Users/ExampleUser/.config/fish' && printf '\\n%s\\n' 'fish_add_path --move \"$HOME/.local/bin\"' >> '/Users/ExampleUser/.config/fish/config.fish'");
        }
    }
}
