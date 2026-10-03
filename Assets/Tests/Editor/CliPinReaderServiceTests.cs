using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Application;
using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies CLI pin reader service file IO behavior.
    /// </summary>
    public sealed class CliPinReaderServiceTests
    {
        [Test]
        public void LoadPinFromPath_WhenPinFileIsValid_ReturnsBothVersions()
        {
            // Tests that a well-formed pin file yields the project runner and dispatcher versions.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(
                    pinPath,
                    "{\"projectRunnerVersion\":\"3.0.0\",\"minimumDispatcherVersion\":\"3.0.1\"}");

                CliPinLoadResult result = CliPinReaderService.LoadPinFromPath(pinPath);

                Assert.That(result.Success, Is.True);
                Assert.That(result.Pin.ProjectRunnerVersion, Is.EqualTo("3.0.0"));
                Assert.That(result.Pin.MinimumDispatcherVersion, Is.EqualTo("3.0.1"));
            }
            finally
            {
                // Why: guard so a failure before directory creation does not mask the original
                // exception with a DirectoryNotFoundException from cleanup.
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadPinFromPath_WhenFileIsMissing_ReturnsFailureWithNotFoundMessage()
        {
            // Tests that a missing pin file fails with the exact "not found" message.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            CliPinLoadResult result = CliPinReaderService.LoadPinFromPath(pinPath);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo($"Unity CLI Loop pin file not found at {pinPath}."));
        }

        [Test]
        public void LoadPinFromPath_WhenFileIsEmpty_ReturnsFailureWithEmptyMessage()
        {
            // Tests that an empty pin file fails with the exact "is empty" message.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(pinPath, "");

                CliPinLoadResult result = CliPinReaderService.LoadPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorMessage, Is.EqualTo($"Unity CLI Loop pin file at {pinPath} is empty."));
            }
            finally
            {
                // Why: guard so a failure before directory creation does not mask the original
                // exception with a DirectoryNotFoundException from cleanup.
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadPinFromPath_WhenProjectRunnerVersionKeyIsMissing_ReturnsFailureWithMissingKeyMessage()
        {
            // Tests that a pin file without projectRunnerVersion fails with the exact missing-key message.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(pinPath, "{\"minimumDispatcherVersion\":\"3.0.1\"}");

                CliPinLoadResult result = CliPinReaderService.LoadPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.ErrorMessage,
                    Is.EqualTo($"Unity CLI Loop pin file at {pinPath} is missing projectRunnerVersion."));
            }
            finally
            {
                // Why: guard so a failure before directory creation does not mask the original
                // exception with a DirectoryNotFoundException from cleanup.
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadPinFromPath_WhenMinimumDispatcherVersionKeyIsMissing_ReturnsFailureWithMissingKeyMessage()
        {
            // Tests that a pin file without minimumDispatcherVersion fails with the exact missing-key message.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(pinPath, "{\"projectRunnerVersion\":\"3.0.0\"}");

                CliPinLoadResult result = CliPinReaderService.LoadPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.ErrorMessage,
                    Is.EqualTo($"Unity CLI Loop pin file at {pinPath} is missing minimumDispatcherVersion."));
            }
            finally
            {
                // Why: guard so a failure before directory creation does not mask the original
                // exception with a DirectoryNotFoundException from cleanup.
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadPinFromPath_WhenFileIsInvalidJson_ReturnsFailureWithInvalidJsonMessage()
        {
            // Tests that a pin file with malformed JSON fails with a message naming the pin path
            // instead of letting the JsonReaderException escape.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(pinPath, "{\"projectRunnerVersion\":");

                CliPinLoadResult result = CliPinReaderService.LoadPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.ErrorMessage,
                    Does.StartWith($"Unity CLI Loop pin file at {pinPath} contains invalid JSON:"));
            }
            finally
            {
                // Why: guard so a failure before directory creation does not mask the original
                // exception with a DirectoryNotFoundException from cleanup.
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenBootstrapFieldsAreMissing_RejectsOnlyBootstrapLoading()
        {
            // Tests that an old pin keeps existing readers working while bootstrap loading fails closed.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(
                    pinPath,
                    "{\"projectRunnerVersion\":\"3.0.0\",\"minimumDispatcherVersion\":\"3.0.1\"}");

                CliPinLoadResult regularResult = CliPinReaderService.LoadPinFromPath(pinPath);
                DispatcherBootstrapPinLoadResult bootstrapResult =
                    CliPinReaderService.LoadDispatcherBootstrapPinFromPath(pinPath);

                Assert.That(regularResult.Success, Is.True);
                Assert.That(bootstrapResult.Success, Is.False);
                Assert.That(bootstrapResult.ErrorMessage, Does.Contain("dispatcherReleaseTag"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenOnlyOneBootstrapFieldExists_ReturnsFailure()
        {
            // Tests that a partially stamped pin is malformed rather than treated as a legacy pin.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(
                    pinPath,
                    "{\"projectRunnerVersion\":\"3.0.0\",\"minimumDispatcherVersion\":\"3.0.1\",\"dispatcherReleaseTag\":\"dispatcher-v3.0.1\"}");

                DispatcherBootstrapPinLoadResult result =
                    CliPinReaderService.LoadDispatcherBootstrapPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorMessage, Does.Contain("both"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenBootstrapFieldsAreEmpty_ReturnsFailure()
        {
            // Tests that empty bootstrap values cannot fall back to an unauthenticated install path.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(
                    pinPath,
                    "{\"projectRunnerVersion\":\"3.0.0\",\"minimumDispatcherVersion\":\"3.0.1\",\"dispatcherReleaseTag\":\"\",\"dispatcherArchiveManifest\":\"\"}");

                DispatcherBootstrapPinLoadResult result =
                    CliPinReaderService.LoadDispatcherBootstrapPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorMessage, Does.Contain("empty"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenManifestIsMalformed_ReturnsFailure()
        {
            // Tests that a manifest entry without an exact SHA-256 digest format fails closed.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(
                    pinPath,
                    "{\"projectRunnerVersion\":\"3.0.0\",\"minimumDispatcherVersion\":\"3.0.1\",\"dispatcherReleaseTag\":\"dispatcher-v3.0.1\",\"dispatcherArchiveManifest\":\"not-a-digest  uloop-dispatcher-darwin-arm64.zip\"}");

                DispatcherBootstrapPinLoadResult result =
                    CliPinReaderService.LoadDispatcherBootstrapPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
                Assert.That(result.ErrorMessage, Does.Contain("invalid dispatcherArchiveManifest entry"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenManifestUsesCrLfOrDuplicateAsset_ReturnsFailure()
        {
            // Tests that bootstrap manifests have one canonical LF-only entry per asset name.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            try
            {
                Directory.CreateDirectory(root);
                string manifest = "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\r\n"
                    + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.sh\n"
                    + "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  install.ps1";
                File.WriteAllText(
                    pinPath,
                    "{\"projectRunnerVersion\":\"3.0.0\",\"minimumDispatcherVersion\":\"3.0.1\",\"dispatcherReleaseTag\":\"dispatcher-v3.0.1\",\"dispatcherArchiveManifest\":\""
                    + manifest.Replace("\r", "\\r").Replace("\n", "\\n")
                    + "\"}");

                DispatcherBootstrapPinLoadResult result =
                    CliPinReaderService.LoadDispatcherBootstrapPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenBootstrapFieldsAreValid_ReturnsPinnedReleaseInputs()
        {
            // Tests that a valid bootstrap pin returns the immutable release tag and complete manifest text.
            string root = CreateTestRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");
            string manifest =
                "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh\n"
                + "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1\n"
                + "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-arm64.zip";

            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(
                    pinPath,
                    "{\"projectRunnerVersion\":\"3.0.0\",\"minimumDispatcherVersion\":\"3.0.1\",\"dispatcherReleaseTag\":\"dispatcher-v3.0.1\",\"dispatcherArchiveManifest\":\""
                    + manifest.Replace("\n", "\\n")
                    + "\"}");

                CliPinLoadResult regularResult = CliPinReaderService.LoadPinFromPath(pinPath);
                DispatcherBootstrapPinLoadResult bootstrapResult =
                    CliPinReaderService.LoadDispatcherBootstrapPinFromPath(pinPath);

                Assert.That(regularResult.Success, Is.True);
                Assert.That(bootstrapResult.Success, Is.True, bootstrapResult.ErrorMessage);
                Assert.That(bootstrapResult.DispatcherReleaseTag, Is.EqualTo("dispatcher-v3.0.1"));
                Assert.That(bootstrapResult.ArchiveManifest, Is.EqualTo(manifest));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, recursive: true);
                }
            }
        }

        private static string CreateTestRoot()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "unity-cli-loop-tests",
                Guid.NewGuid().ToString("N"));
        }

        private const string PosixInstallerEntry =
            "aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa  install.sh";
        private const string WindowsInstallerEntry =
            "bbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb  install.ps1";
        private const string ArchiveEntry =
            "cccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccccc  uloop-dispatcher-darwin-arm64.zip";

        /// <summary>
        /// Verifies that a whitespace-only pin path fails with the empty-path message before touching the file system.
        /// </summary>
        [Test]
        public void LoadPinFromPath_WhenPathIsWhitespace_ReturnsEmptyPathFailure()
        {
            CliPinLoadResult result = CliPinReaderService.LoadPinFromPath("   ");

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo("Unity CLI Loop pin file path is empty."));
        }

        /// <summary>
        /// Verifies that bootstrap loading forwards the regular pin load failure message unchanged.
        /// </summary>
        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenPinFileIsMissing_ReturnsRegularLoadFailure()
        {
            string root = CreateUniqueTempRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");

            DispatcherBootstrapPinLoadResult result = CliPinReaderService.LoadDispatcherBootstrapPinFromPath(pinPath);

            Assert.That(result.Success, Is.False);
            Assert.That(result.ErrorMessage, Is.EqualTo($"Unity CLI Loop pin file not found at {pinPath}."));
        }

        /// <summary>
        /// Verifies that a release tag without the dispatcher prefix is rejected as invalid.
        /// </summary>
        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenReleaseTagLacksDispatcherPrefix_ReturnsInvalidTagFailure()
        {
            AssertBootstrapFailure(
                "v3.0.1",
                BuildManifest(PosixInstallerEntry, WindowsInstallerEntry, ArchiveEntry),
                "defines an invalid dispatcherReleaseTag.");
        }

        /// <summary>
        /// Verifies that a release tag containing a character outside letters, digits, dots and hyphens is rejected.
        /// </summary>
        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenReleaseTagContainsSlash_ReturnsInvalidTagFailure()
        {
            AssertBootstrapFailure(
                "dispatcher-v3.0.1/evil",
                BuildManifest(PosixInstallerEntry, WindowsInstallerEntry, ArchiveEntry),
                "defines an invalid dispatcherReleaseTag.");
        }

        /// <summary>
        /// Verifies that an LF-only manifest listing the same asset twice is rejected.
        /// </summary>
        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenManifestRepeatsAssetName_ReturnsInvalidManifestFailure()
        {
            AssertBootstrapFailure(
                "dispatcher-v3.0.1",
                BuildManifest(
                    PosixInstallerEntry,
                    "dddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddddd  install.sh",
                    WindowsInstallerEntry),
                "contains an invalid dispatcherArchiveManifest entry.");
        }

        /// <summary>
        /// Verifies that a manifest digest containing a non-hexadecimal character is rejected.
        /// </summary>
        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenManifestDigestIsNotHex_ReturnsInvalidManifestFailure()
        {
            AssertBootstrapFailure(
                "dispatcher-v3.0.1",
                BuildManifest(
                    "gggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggggg  install.sh",
                    WindowsInstallerEntry,
                    ArchiveEntry),
                "contains an invalid dispatcherArchiveManifest entry.");
        }

        /// <summary>
        /// Verifies that a manifest without the Windows installer digest is rejected.
        /// </summary>
        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenWindowsInstallerDigestIsMissing_ReturnsMissingDigestFailure()
        {
            AssertBootstrapFailure(
                "dispatcher-v3.0.1",
                BuildManifest(PosixInstallerEntry, ArchiveEntry, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  checksums.txt"),
                "is missing an installer script digest.");
        }

        /// <summary>
        /// Verifies that a manifest without the POSIX installer digest is rejected.
        /// </summary>
        [Test]
        public void LoadDispatcherBootstrapPinFromPath_WhenPosixInstallerDigestIsMissing_ReturnsMissingDigestFailure()
        {
            AssertBootstrapFailure(
                "dispatcher-v3.0.1",
                BuildManifest(WindowsInstallerEntry, ArchiveEntry, "eeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeeee  checksums.txt"),
                "is missing an installer script digest.");
        }

        private static void AssertBootstrapFailure(string releaseTag, string manifest, string expectedMessageSuffix)
        {
            string root = CreateUniqueTempRoot();
            string pinPath = Path.Combine(root, "project-runner-pin.json");
            try
            {
                Directory.CreateDirectory(root);
                File.WriteAllText(
                    pinPath,
                    "{\"projectRunnerVersion\":\"3.0.0\",\"minimumDispatcherVersion\":\"3.0.1\",\"dispatcherReleaseTag\":\""
                    + releaseTag
                    + "\",\"dispatcherArchiveManifest\":\""
                    + manifest.Replace("\n", "\\n")
                    + "\"}");

                DispatcherBootstrapPinLoadResult result = CliPinReaderService.LoadDispatcherBootstrapPinFromPath(pinPath);

                Assert.That(result.Success, Is.False);
                Assert.That(
                    result.ErrorMessage,
                    Is.EqualTo($"Unity CLI Loop pin file at {pinPath} {expectedMessageSuffix}"));
            }
            finally
            {
                if (Directory.Exists(root))
                {
                    Directory.Delete(root, true);
                }
            }
        }

        private static string BuildManifest(string firstEntry, string secondEntry, string thirdEntry)
        {
            return firstEntry + "\n" + secondEntry + "\n" + thirdEntry;
        }

        private static string CreateUniqueTempRoot()
        {
            return Path.Combine(Path.GetTempPath(), "uloop-test-" + Guid.NewGuid().ToString("N"));
        }
    }
}
