using System.IO;
using System.Runtime.InteropServices;

using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies Project Local CLI Installer behavior.
    /// </summary>
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
        public void GetProjectCliBundlePath_UsesPackagedCoreBinary()
        {
            // Verifies that project-local installs use the packaged core binary, not the global dispatcher.
            string result = ProjectLocalCliInstaller.GetProjectCliBundlePath();

            Assert.That(result, Does.Contain(Path.Combine("Cli~", "Core~", "dist")));
            string expectedFileName = UnityEngine.Application.platform == RuntimePlatform.WindowsEditor
                ? "uloop-core.exe"
                : "uloop-core";
            Assert.That(Path.GetFileName(result), Is.EqualTo(expectedFileName));
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
            string expectedFileName = UnityEngine.Application.platform == RuntimePlatform.WindowsEditor
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
            Assert.That(result.ErrorOutput, Does.Contain(UnityEngine.Application.platform.ToString()));
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

        [Test]
        public void DetectCliOutput_WithRequiredDispatcherVersionFlag_ReturnsRequirement()
        {
            // Verifies that setup can query the bundled core for its required dispatcher version.
            if (UnityEngine.Application.platform == RuntimePlatform.WindowsEditor)
            {
                Assert.Ignore("This test uses a POSIX shell stub and is not executable on Windows.");
            }

            string sourceBundlePath = Path.Combine(_temporaryRoot, "source-cli.cjs");
            File.WriteAllText(sourceBundlePath, BuildVersionScript("3.0.0-beta.1", "source"));

            string projectRoot = Path.Combine(_temporaryRoot, "Project");
            Directory.CreateDirectory(projectRoot);
            CliInstallResult initialResult = ProjectLocalCliInstaller.InstallProjectLocalCliFromBundle(
                sourceBundlePath,
                projectRoot);
            Assert.That(initialResult.Success, Is.True, initialResult.ErrorOutput);
            string projectLocalCliPath = ProjectLocalCliInstaller.GetProjectLocalCliPath(projectRoot);

            string requiredDispatcherVersion = ProjectLocalCliInstaller.DetectCliOutput(
                projectLocalCliPath,
                projectRoot,
                CliConstants.REQUIRED_DISPATCHER_VERSION_FLAG);

            Assert.That(requiredDispatcherVersion, Is.EqualTo("3.0.0-beta.1"));
        }

        [Test]
        public void CliContractReader_ReadsCoreDispatcherRequirement()
        {
            // Verifies that setup can read core compatibility from contract when the bundled binary cannot answer.
            string packageRoot = CreatePackageRootWithCoreContract("3.0.0-beta.2");

            string requiredDispatcherVersion = CliContractReader.GetMinimumRequiredDispatcherVersion(packageRoot);

            Assert.That(requiredDispatcherVersion, Is.EqualTo("3.0.0-beta.2"));
        }

        [Test]
        public void LayoutContract_MatchesEditorCliConstants()
        {
            // Verifies that editor path constants stay aligned with the CLI layout manifest.
            string path = Path.Combine(
                UnityCliLoopConstants.PackageResolvedPath,
                CliConstants.CLI_PACKAGE_DIR_NAME,
                CliConstants.CLI_LAYOUT_CONTRACT_FILE_NAME);

            JObject contract = JObject.Parse(File.ReadAllText(path));
            JObject layout = (JObject)contract["layout"];
            JObject binaries = (JObject)contract["binaries"];
            JObject core = (JObject)binaries["core"];
            JObject dispatcher = (JObject)binaries["dispatcher"];

            Assert.That(layout["cliDir"].Value<string>(), Is.EqualTo(CliConstants.CLI_PACKAGE_DIR_NAME));
            Assert.That(layout["coreDir"].Value<string>(), Is.EqualTo(CliConstants.GO_CLI_CORE_DIR_NAME));
            Assert.That(layout["dispatcherDir"].Value<string>(), Is.EqualTo(CliConstants.GO_CLI_DISPATCHER_DIR_NAME));
            Assert.That(layout["sharedDir"].Value<string>(), Is.EqualTo(CliConstants.GO_CLI_SHARED_DIR_NAME));
            Assert.That(layout["distDir"].Value<string>(), Is.EqualTo(CliConstants.DIST_DIR_NAME));
            Assert.That(layout["projectLocalBinDir"].Value<string>(), Is.EqualTo(CliConstants.PROJECT_LOCAL_BIN_DIR_NAME));
            Assert.That(core["unix"].Value<string>(), Is.EqualTo(CliConstants.PROJECT_LOCAL_UNIX_COMMAND_NAME));
            Assert.That(core["windows"].Value<string>(), Is.EqualTo(CliConstants.PROJECT_LOCAL_WINDOWS_COMMAND_NAME));
            Assert.That(dispatcher["unix"].Value<string>(), Is.EqualTo(CliConstants.GLOBAL_DISPATCHER_UNIX_BUNDLE_NAME));
            Assert.That(dispatcher["windows"].Value<string>(), Is.EqualTo(CliConstants.GLOBAL_DISPATCHER_WINDOWS_BUNDLE_NAME));
        }

        private static string BuildVersionScript(string version, string marker)
        {
            return "#!/bin/sh\n"
                + "if [ \"$1\" = \"--version\" ]; then\n"
                + $"  echo \"{version}\"\n"
                + "  exit 0\n"
                + "fi\n"
                + $"if [ \"$1\" = \"{CliConstants.REQUIRED_DISPATCHER_VERSION_FLAG}\" ]; then\n"
                + $"  echo \"{version}\"\n"
                + "  exit 0\n"
                + "fi\n"
                + $"echo \"{marker}\"\n";
        }

        private string CreatePackageRootWithCoreContract(string requiredDispatcherVersion)
        {
            string packageRoot = Path.Combine(_temporaryRoot, "package");
            string coreRoot = Path.Combine(
                packageRoot,
                CliConstants.CLI_PACKAGE_DIR_NAME,
                CliConstants.GO_CLI_CORE_DIR_NAME);
            Directory.CreateDirectory(coreRoot);
            string contract = "{\n"
                + "  \"schemaVersion\": 1,\n"
                + "  \"coreVersion\": \"3.0.0-beta.2\",\n"
                + $"  \"minimumRequiredDispatcherVersion\": \"{requiredDispatcherVersion}\",\n"
                + $"  \"requiredDispatcherVersionFlag\": \"{CliConstants.REQUIRED_DISPATCHER_VERSION_FLAG}\"\n"
                + "}\n";
            File.WriteAllText(Path.Combine(coreRoot, CliConstants.CLI_CONTRACT_FILE_NAME), contract);
            return packageRoot;
        }
    }
}
