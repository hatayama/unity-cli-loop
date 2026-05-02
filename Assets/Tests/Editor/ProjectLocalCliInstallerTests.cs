using System.IO;
using System.Runtime.InteropServices;

using NUnit.Framework;
using UnityEngine;

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
        public void InstallProjectLocalCliFromBundle_CopiesNativeCli()
        {
            string sourceBundlePath = Path.Combine(_temporaryRoot, "source-cli");
            File.WriteAllText(sourceBundlePath, BuildVersionScript("3.0.0-beta.0", "source"));

            string projectRoot = Path.Combine(_temporaryRoot, "Project");
            Directory.CreateDirectory(projectRoot);

            CliInstallResult result = ProjectLocalCliInstaller.InstallProjectLocalCliFromBundle(
                sourceBundlePath,
                projectRoot);

            string projectLocalCliPath = ProjectLocalCliInstaller.GetProjectLocalCliPath(projectRoot);

            Assert.That(result.Success, Is.True, result.ErrorOutput);
            Assert.That(File.Exists(projectLocalCliPath), Is.True);
            string expectedFileName = Application.platform == RuntimePlatform.WindowsEditor
                ? "uloop-core.exe"
                : "uloop-core";
            Assert.That(Path.GetFileName(projectLocalCliPath), Is.EqualTo(expectedFileName));
            Assert.That(File.ReadAllText(projectLocalCliPath), Is.EqualTo(File.ReadAllText(sourceBundlePath)));
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
            Assert.That(result.ErrorOutput, Does.Contain(Application.platform.ToString()));
            Assert.That(result.ErrorOutput, Does.Contain(RuntimeInformation.ProcessArchitecture.ToString()));
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
        public void EnsureProjectLocalCliCurrentFromBundle_WhenVersionAndBinaryAreCurrent_DoesNotOverwriteBundle()
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

            CliInstallResult result = ProjectLocalCliAutoInstaller.EnsureProjectLocalCliCurrentFromBundle(
                currentBundlePath,
                projectRoot,
                "3.0.0-beta.0");

            string projectLocalCliPath = ProjectLocalCliInstaller.GetProjectLocalCliPath(projectRoot);
            Assert.That(result.Success, Is.True, result.ErrorOutput);
            Assert.That(File.ReadAllText(projectLocalCliPath), Is.EqualTo(currentBundleContent));
        }

        [Test]
        public void EnsureProjectLocalCliCurrentFromBundle_WhenVersionMatchesButBinaryDiffers_CopiesBundle()
        {
            string oldBundlePath = Path.Combine(_temporaryRoot, "old-cli.cjs");
            File.WriteAllText(oldBundlePath, BuildVersionScript("3.0.0-beta.0", "old"));

            string projectRoot = Path.Combine(_temporaryRoot, "Project");
            Directory.CreateDirectory(projectRoot);
            CliInstallResult initialResult = ProjectLocalCliInstaller.InstallProjectLocalCliFromBundle(
                oldBundlePath,
                projectRoot);
            Assert.That(initialResult.Success, Is.True, initialResult.ErrorOutput);

            string replacementBundlePath = Path.Combine(_temporaryRoot, "replacement-cli.cjs");
            string replacementBundleContent = BuildVersionScript("3.0.0-beta.0", "replacement");
            File.WriteAllText(replacementBundlePath, replacementBundleContent);

            CliInstallResult result = ProjectLocalCliAutoInstaller.EnsureProjectLocalCliCurrentFromBundle(
                replacementBundlePath,
                projectRoot,
                "3.0.0-beta.0");

            string projectLocalCliPath = ProjectLocalCliInstaller.GetProjectLocalCliPath(projectRoot);
            Assert.That(result.Success, Is.True, result.ErrorOutput);
            Assert.That(File.ReadAllText(projectLocalCliPath), Is.EqualTo(replacementBundleContent));
        }

        private static string BuildVersionScript(string version, string marker)
        {
            return "#!/bin/sh\n"
                + "if [ \"$1\" = \"--version\" ]; then\n"
                + $"  echo \"{version}\"\n"
                + "  exit 0\n"
                + "fi\n"
                + $"echo \"{marker}\"\n";
        }
    }
}
