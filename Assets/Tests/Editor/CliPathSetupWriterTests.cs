using System;
using System.Collections.Generic;
using System.IO;
using System.Security;

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

        /// <summary>
        /// Verifies a PATH line with spaces around '=' is not taken as existing setup, because POSIX shells do not parse it as an assignment.
        /// </summary>
        [Test]
        public void Apply_WhenOnlyLineHasSpacesAroundAssignment_AppendsSetupLine()
        {
            CliPathSetupPlan plan = CreateZshPlan();
            List<string> appendedContent = new List<string>();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "PATH = \"$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendedContent.Add(content));

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendedContent, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies an export line with a space after '=' is not taken as existing setup, because the shell assigns an empty PATH there.
        /// </summary>
        [Test]
        public void Apply_WhenOnlyLineHasSpaceAfterAssignment_AppendsSetupLine()
        {
            CliPathSetupPlan plan = CreateZshPlan();
            List<string> appendedContent = new List<string>();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "export PATH= \"$HOME/.local/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendedContent.Add(content));

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendedContent, Has.Count.EqualTo(1));
        }

        /// <summary>
        /// Verifies a later line with spaces around '=' does not count as a PATH setup that shadows the canonical line.
        /// </summary>
        [Test]
        public void Apply_WhenCanonicalLineIsFollowedBySpacedNonAssignment_DoesNotAppend()
        {
            CliPathSetupPlan plan = CreateZshPlan();
            List<string> appendedContent = new List<string>();

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => true,
                path => "export PATH=\"$HOME/.local/bin:$PATH\"\nPATH = \"/opt/other/bin:$PATH\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendedContent.Add(content));

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendedContent, Has.Count.EqualTo(0));
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

        /// <summary>
        /// Verifies that a plan that cannot be applied automatically is reported as unsupported without touching the profile.
        /// </summary>
        [Test]
        public void Apply_WhenPlanCannotApplyAutomatically_ReturnsUnsupportedWithoutFileAccess()
        {
            CliPathSetupPlan plan = new CliPathSetupPlan(
                CliPathSetupShellKind.Unsupported,
                "tcsh",
                false,
                "<HOME>/.local/bin",
                "$HOME/.local/bin",
                "",
                "",
                "");
            int fileAccessCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                plan,
                path => { fileAccessCount++; return true; },
                path => { fileAccessCount++; return string.Empty; },
                path => { fileAccessCount++; return new DirectoryInfo(path); },
                (path, content) => fileAccessCount++);

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Unsupported));
            Assert.That(result.ErrorOutput, Is.EqualTo("This shell is not supported for automatic PATH setup."));
            Assert.That(fileAccessCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that a security-policy denial while reading the profile falls back to the failed result path.
        /// </summary>
        [Test]
        public void Apply_WhenProfileAccessIsDeniedBySecurityPolicy_ReturnsFailedResult()
        {
            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                CreateZshPlan(),
                path => throw new SecurityException("profile access denied by policy"),
                path => string.Empty,
                path => new DirectoryInfo(path),
                (path, content) => { });

            Assert.That(result.Success, Is.False);
            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Failed));
            Assert.That(result.ErrorOutput, Is.EqualTo("profile access denied by policy"));
        }

        /// <summary>
        /// Verifies that a non-canonical fish_add_path line that prepends the install directory counts as already configured.
        /// </summary>
        [Test]
        public void Apply_WhenFishAddPathPrependsInstallDirectory_DoesNotAppend()
        {
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                CreateFishPlan(),
                path => true,
                path => "fish_add_path \"$HOME/.local/bin\"\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that fish_add_path in append mode does not count as setup because it puts the install directory last.
        /// </summary>
        [TestCase("fish_add_path --append \"$HOME/.local/bin\"")]
        [TestCase("fish_add_path -a \"$HOME/.local/bin\"")]
        public void Apply_WhenFishAddPathAppendsInstallDirectory_Appends(string profileLine)
        {
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                CreateFishPlan(),
                path => true,
                path => profileLine + "\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
            Assert.That(appendCount, Is.EqualTo(1));
        }

        /// <summary>
        /// Verifies that fish commands that do not set PATH after a valid setup line do not hide that setup line.
        /// </summary>
        [TestCase("echo $HOME/.local/bin")]
        [TestCase("set -U")]
        public void Apply_WhenFishSetupIsFollowedByNonPathCommand_DoesNotAppend(string trailingLine)
        {
            int appendCount = 0;

            CliPathSetupApplyResult result = CliPathSetupWriter.Apply(
                CreateFishPlan(),
                path => true,
                path => "fish_add_path \"$HOME/.local/bin\"\n" + trailingLine + "\n",
                path => new DirectoryInfo(path),
                (path, content) => appendCount++);

            Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.AlreadyConfigured));
            Assert.That(appendCount, Is.EqualTo(0));
        }

        /// <summary>
        /// Verifies that applying to the real file system creates the missing profile directory and writes exactly the setup line (temp dir).
        /// </summary>
        [Test]
        public void ApplyToFileSystem_WhenProfileIsMissing_CreatesProfileWithSetupLine()
        {
            string root = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            string profilePath = Path.Combine(root, "config", ".zshrc");
            CliPathSetupPlan plan = new CliPathSetupPlan(
                CliPathSetupShellKind.Zsh,
                "zsh",
                true,
                "<HOME>/.local/bin",
                "$HOME/.local/bin",
                profilePath,
                "export PATH=\"$HOME/.local/bin:$PATH\"",
                "echo setup");
            try
            {
                CliPathSetupApplyResult result = CliPathSetupWriter.ApplyToFileSystem(plan);

                Assert.That(result.Success, Is.True);
                Assert.That(result.Status, Is.EqualTo(CliPathSetupApplyStatus.Applied));
                Assert.That(File.Exists(profilePath), Is.True);
                Assert.That(File.ReadAllText(profilePath), Is.EqualTo("export PATH=\"$HOME/.local/bin:$PATH\"\n"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }
    }
}
