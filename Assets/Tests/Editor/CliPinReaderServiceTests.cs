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
                Directory.Delete(root, recursive: true);
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
                Directory.Delete(root, recursive: true);
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
                Directory.Delete(root, recursive: true);
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
                Directory.Delete(root, recursive: true);
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
