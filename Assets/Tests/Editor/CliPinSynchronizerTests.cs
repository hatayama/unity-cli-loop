using System;
using System.IO;

using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.Infrastructure;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    public sealed class CliPinSynchronizerTests
    {
        [Test]
        public void SyncProjectPinFile_WhenDestinationMissing_ShouldCopyPackagePin()
        {
            // Tests that the project runner pin contract is published into the project .uloop directory.
            string root = CreateTestRoot();
            string packageRoot = Path.Combine(root, "package");
            string projectRoot = Path.Combine(root, "project");

            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(projectRoot);
                File.WriteAllText(
                    Path.Combine(packageRoot, UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME),
                    "{\"projectRunnerVersion\":\"3.0.0\"}");

                bool changed = CliPinSynchronizer.SyncProjectPinFile(packageRoot, projectRoot);

                Assert.That(changed, Is.True);
                Assert.That(
                    File.ReadAllText(
                        Path.Combine(
                            projectRoot,
                            UnityCliLoopConstants.ULOOP_DIR,
                            UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME)),
                    Is.EqualTo("{\"projectRunnerVersion\":\"3.0.0\"}"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void SyncProjectPinFile_WhenDestinationMatches_ShouldSkipWrite()
        {
            // Tests that startup does not rewrite the project pin when package and project copies already match.
            string root = CreateTestRoot();
            string packageRoot = Path.Combine(root, "package");
            string projectRoot = Path.Combine(root, "project");
            string projectUloopRoot = Path.Combine(projectRoot, UnityCliLoopConstants.ULOOP_DIR);

            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(projectUloopRoot);
                string sourcePath = Path.Combine(
                    packageRoot,
                    UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
                string destinationPath = Path.Combine(
                    projectUloopRoot,
                    UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
                File.WriteAllText(sourcePath, "{\"projectRunnerVersion\":\"3.0.0\"}");
                File.WriteAllText(destinationPath, "{\"projectRunnerVersion\":\"3.0.0\"}");
                DateTime previousWriteTime = File.GetLastWriteTimeUtc(destinationPath);

                bool changed = CliPinSynchronizer.SyncProjectPinFile(packageRoot, projectRoot);

                Assert.That(changed, Is.False);
                Assert.That(File.GetLastWriteTimeUtc(destinationPath), Is.EqualTo(previousWriteTime));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void SyncProjectPinFile_WhenPackagePinChanges_ShouldUpdateProjectPin()
        {
            // Tests that package upgrades update the project runner pin contract.
            string root = CreateTestRoot();
            string packageRoot = Path.Combine(root, "package");
            string projectRoot = Path.Combine(root, "project");
            string projectUloopRoot = Path.Combine(projectRoot, UnityCliLoopConstants.ULOOP_DIR);

            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(projectUloopRoot);
                File.WriteAllText(
                    Path.Combine(packageRoot, UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME),
                    "{\"projectRunnerVersion\":\"3.0.1\"}");
                string destinationPath = Path.Combine(
                    projectUloopRoot,
                    UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
                File.WriteAllText(destinationPath, "{\"projectRunnerVersion\":\"3.0.0\"}");

                bool changed = CliPinSynchronizer.SyncProjectPinFile(packageRoot, projectRoot);

                Assert.That(changed, Is.True);
                Assert.That(File.ReadAllText(destinationPath), Is.EqualTo("{\"projectRunnerVersion\":\"3.0.1\"}"));
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void SyncProjectPinFile_WhenSourcePinMissing_ShouldLogWarningAndSkip()
        {
            // Tests that a missing package source pin now emits a warning instead of silently returning false.
            string root = CreateTestRoot();
            string packageRoot = Path.Combine(root, "package");
            string projectRoot = Path.Combine(root, "project");

            try
            {
                Directory.CreateDirectory(packageRoot);
                Directory.CreateDirectory(projectRoot);
                string sourcePath = Path.Combine(
                    packageRoot,
                    UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME);
                LogAssert.Expect(
                    LogType.Warning,
                    $"Unity CLI Loop skipped {UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME} synchronization because the package source pin was not found at {sourcePath}.");

                bool changed = CliPinSynchronizer.SyncProjectPinFile(packageRoot, projectRoot);

                Assert.That(changed, Is.False);
                Assert.That(File.Exists(sourcePath), Is.False);
                Assert.That(
                    File.Exists(
                        Path.Combine(
                            projectRoot,
                            UnityCliLoopConstants.ULOOP_DIR,
                            UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME)),
                    Is.False);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void SyncProjectPinFile_WhenPackageRootMissing_ShouldSkipWrite()
        {
            // Tests that startup skips pin synchronization while Unity package resolution is incomplete.
            string root = CreateTestRoot();
            string projectRoot = Path.Combine(root, "project");

            try
            {
                Directory.CreateDirectory(projectRoot);
                string expectedWarning =
                    $"Unity CLI Loop skipped {UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME} synchronization because the package root is empty.";
                LogAssert.Expect(
                    LogType.Warning,
                    expectedWarning);

                bool changed = CliPinSynchronizer.SyncProjectPinFile(string.Empty, projectRoot);

                Assert.That(changed, Is.False);
                Assert.That(
                    File.Exists(
                        Path.Combine(
                            projectRoot,
                            UnityCliLoopConstants.ULOOP_DIR,
                            UnityCliLoopConstants.ULOOP_PROJECT_RUNNER_PIN_FILE_NAME)),
                    Is.False);
            }
            finally
            {
                Directory.Delete(root, recursive: true);
            }
        }

        [Test]
        public void ResolveCurrentProjectRoot_WhenAssetsPathProvided_ShouldReturnUnityProjectRoot()
        {
            // Tests that startup derives the Unity project root from Application.dataPath semantics.
            string projectRoot = Path.Combine(CreateTestRoot(), "UnityProject");
            string assetsPath = Path.Combine(projectRoot, "Assets");

            string resolvedProjectRoot = CliPinSynchronizer.ResolveCurrentProjectRoot(assetsPath);

            Assert.That(resolvedProjectRoot, Is.EqualTo(projectRoot));
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
