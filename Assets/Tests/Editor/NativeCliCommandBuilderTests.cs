using System;
using System.IO;

using NUnit.Framework;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies package-local installer resolution and manifest digest validation of the native CLI command builder.
    /// </summary>
    public sealed class NativeCliCommandBuilderTests
    {
        private const string WindowsInstallerEntry =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1";

        private string _root;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, true);
            }
        }

        /// <summary>
        /// Verifies that a package folder not named src is not treated as the repository checkout even when the script exists (temp dir).
        /// </summary>
        [Test]
        public void ResolvePackageLocalInstallerScriptPath_WhenPackageFolderIsNotSrc_ReturnsNull()
        {
            CreateScript("install.sh");
            string packagePath = CreateDirectory(Path.Combine("Packages", "com.example.package"));

            string result = NativeCliCommandBuilder.ResolvePackageLocalInstallerScriptPath(packagePath, "install.sh");

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that a src folder outside a Packages folder is not treated as the repository checkout even when the script exists (temp dir).
        /// </summary>
        [Test]
        public void ResolvePackageLocalInstallerScriptPath_WhenParentFolderIsNotPackages_ReturnsNull()
        {
            CreateScript("install.sh");
            string packagePath = CreateDirectory(Path.Combine("Library", "src"));

            string result = NativeCliCommandBuilder.ResolvePackageLocalInstallerScriptPath(packagePath, "install.sh");

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that a repository checkout without the requested installer script resolves to no local script (temp dir).
        /// </summary>
        [Test]
        public void ResolvePackageLocalInstallerScriptPath_WhenScriptIsMissing_ReturnsNull()
        {
            CreateScript("install.ps1");
            string packagePath = CreateDirectory(Path.Combine("Packages", "src"));

            string result = NativeCliCommandBuilder.ResolvePackageLocalInstallerScriptPath(packagePath, "install.sh");

            Assert.That(result, Is.Null);
        }

        /// <summary>
        /// Verifies that a manifest digest with the wrong length cannot build a remote install command.
        /// </summary>
        [Test]
        public void BuildRemoteInstallCommand_WhenInstallerDigestHasWrongLength_ThrowsArgumentException()
        {
            string manifest = "abc123  install.sh\n" + WindowsInstallerEntry;

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => NativeCliCommandBuilder.BuildRemoteInstallCommand(
                    RuntimePlatform.OSXEditor,
                    "dispatcher-v3.0.1",
                    manifest,
                    false,
                    "/bin/zsh"));

            Assert.That(exception.ParamName, Is.EqualTo("archiveManifest"));
            Assert.That(
                exception.Message,
                Does.StartWith("dispatcher archive manifest is missing a valid digest for install.sh"));
        }

        /// <summary>
        /// Verifies that a 64-character manifest digest containing a non-hexadecimal character cannot build a remote install command.
        /// </summary>
        [Test]
        public void BuildRemoteInstallCommand_WhenInstallerDigestIsNotHex_ThrowsArgumentException()
        {
            string manifest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaz  install.sh\n" + WindowsInstallerEntry;

            ArgumentException exception = Assert.Throws<ArgumentException>(
                () => NativeCliCommandBuilder.BuildRemoteInstallCommand(
                    RuntimePlatform.OSXEditor,
                    "dispatcher-v3.0.1",
                    manifest,
                    false,
                    "/bin/zsh"));

            Assert.That(exception.ParamName, Is.EqualTo("archiveManifest"));
            Assert.That(
                exception.Message,
                Does.StartWith("dispatcher archive manifest is missing a valid digest for install.sh"));
        }

        private string CreateDirectory(string relativePath)
        {
            string path = Path.Combine(_root, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        private void CreateScript(string scriptName)
        {
            string scriptsDirectory = CreateDirectory("scripts");
            File.WriteAllText(Path.Combine(scriptsDirectory, scriptName), "#!/bin/sh\n");
        }
    }
}
