using System.IO;

using NUnit.Framework;

namespace io.github.hatayama.uLoopMCP.Tests.Editor
{
    public class ProjectLocalCliInstallerTests
    {
        private string _temporaryRoot;

        [SetUp]
        public void SetUp()
        {
            _temporaryRoot = Path.Combine(
                Path.GetTempPath(),
                "uloop-project-local-cli-tests-" + System.Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_temporaryRoot);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_temporaryRoot))
            {
                Directory.Delete(_temporaryRoot, recursive: true);
            }
        }

        [Test]
        public void InstallProjectLocalCliFromBundle_CopiesCliAndWindowsShim()
        {
            string sourceBundlePath = Path.Combine(_temporaryRoot, "source-cli.cjs");
            File.WriteAllText(sourceBundlePath, "#!/usr/bin/env node\nconsole.log('project cli');\n");

            string projectRoot = Path.Combine(_temporaryRoot, "Project");
            Directory.CreateDirectory(projectRoot);

            CliInstallResult result = ProjectLocalCliInstaller.InstallProjectLocalCliFromBundle(
                sourceBundlePath,
                projectRoot);

            string projectLocalCliPath = ProjectLocalCliInstaller.GetProjectLocalCliPath(projectRoot);
            string windowsCommandPath = ProjectLocalCliInstaller.GetProjectLocalWindowsCommandPath(projectRoot);

            Assert.That(result.Success, Is.True, result.ErrorOutput);
            Assert.That(File.Exists(projectLocalCliPath), Is.True);
            Assert.That(File.ReadAllText(projectLocalCliPath), Is.EqualTo(File.ReadAllText(sourceBundlePath)));
            Assert.That(File.Exists(windowsCommandPath), Is.True);
            Assert.That(File.ReadAllText(windowsCommandPath), Does.Contain("node \"%~dp0\\uloop.cjs\" %*"));
        }

        [Test]
        public void InstallProjectLocalCliFromBundle_ReturnsFailureWhenBundleIsMissing()
        {
            string missingSourceBundlePath = Path.Combine(_temporaryRoot, "missing.cjs");
            string projectRoot = Path.Combine(_temporaryRoot, "Project");
            Directory.CreateDirectory(projectRoot);

            CliInstallResult result = ProjectLocalCliInstaller.InstallProjectLocalCliFromBundle(
                missingSourceBundlePath,
                projectRoot);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorOutput, Does.Contain(missingSourceBundlePath));
        }

        [Test]
        public void IsProjectLocalCliVersionCurrent_WhenCliIsMissing_ReturnsFalse()
        {
            string projectRoot = Path.Combine(_temporaryRoot, "Project");
            Directory.CreateDirectory(projectRoot);

            bool isCurrent = ProjectLocalCliInstaller.IsProjectLocalCliVersionCurrent(projectRoot, "3.0.0");

            Assert.That(isCurrent, Is.False);
        }

        [Test]
        public void EnsureProjectLocalCliCurrentFromBundle_WhenCliIsMissing_CopiesBundle()
        {
            string sourceBundlePath = Path.Combine(_temporaryRoot, "source-cli.cjs");
            File.WriteAllText(sourceBundlePath, BuildVersionScript("3.0.0-beta.0", "source"));

            string projectRoot = Path.Combine(_temporaryRoot, "Project");
            Directory.CreateDirectory(projectRoot);

            CliInstallResult result = ProjectLocalCliAutoInstaller.EnsureProjectLocalCliCurrentFromBundle(
                sourceBundlePath,
                projectRoot,
                "3.0.0-beta.0");

            string projectLocalCliPath = ProjectLocalCliInstaller.GetProjectLocalCliPath(projectRoot);
            Assert.That(result.Success, Is.True, result.ErrorOutput);
            Assert.That(File.Exists(projectLocalCliPath), Is.True);
            Assert.That(File.ReadAllText(projectLocalCliPath), Is.EqualTo(File.ReadAllText(sourceBundlePath)));
        }

        [Test]
        public void EnsureProjectLocalCliCurrentFromBundle_WhenVersionIsCurrent_DoesNotOverwriteBundle()
        {
            string currentBundlePath = Path.Combine(_temporaryRoot, "current-cli.cjs");
            string currentBundleContent = BuildVersionScript("3.0.0-beta.0", "current");
            File.WriteAllText(currentBundlePath, currentBundleContent);

            string projectRoot = Path.Combine(_temporaryRoot, "Project");
            Directory.CreateDirectory(projectRoot);
            CliInstallResult initialResult = ProjectLocalCliInstaller.InstallProjectLocalCliFromBundle(
                currentBundlePath,
                projectRoot);
            Assert.That(initialResult.Success, Is.True, initialResult.ErrorOutput);

            string replacementBundlePath = Path.Combine(_temporaryRoot, "replacement-cli.cjs");
            File.WriteAllText(replacementBundlePath, BuildVersionScript("3.0.0-beta.0", "replacement"));

            CliInstallResult result = ProjectLocalCliAutoInstaller.EnsureProjectLocalCliCurrentFromBundle(
                replacementBundlePath,
                projectRoot,
                "3.0.0-beta.0");

            string projectLocalCliPath = ProjectLocalCliInstaller.GetProjectLocalCliPath(projectRoot);
            Assert.That(result.Success, Is.True, result.ErrorOutput);
            Assert.That(File.ReadAllText(projectLocalCliPath), Is.EqualTo(currentBundleContent));
        }

        private static string BuildVersionScript(string version, string marker)
        {
            return "#!/usr/bin/env node\n"
                + "if (process.argv.includes('--version')) {\n"
                + $"  console.log('{version}');\n"
                + "  process.exit(0);\n"
                + "}\n"
                + $"console.log('{marker}');\n";
        }
    }
}
