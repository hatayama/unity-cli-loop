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
    }
}
