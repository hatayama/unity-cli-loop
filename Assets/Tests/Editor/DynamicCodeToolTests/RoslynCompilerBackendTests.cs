using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class RoslynCompilerBackendTests
    {
        private string _tempDirectoryPath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectoryPath = Path.Combine(Path.GetTempPath(), $"RoslynCompilerBackendTests_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectoryPath);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_tempDirectoryPath))
            {
                Directory.Delete(_tempDirectoryPath, true);
            }
        }

        [Test]
        public async Task AwaitOneShotProcessCompletionAsync_WhenProcessCompletes_ShouldReturnOutputAndExitCode()
        {
            TaskCompletionSource<string> stdoutCompletionSource =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<string> stderrCompletionSource =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> exitCompletionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            stdoutCompletionSource.SetResult("stdout");
            stderrCompletionSource.SetResult("stderr");
            exitCompletionSource.SetResult(true);

            RoslynCompilerBackend.OneShotProcessCompletionResult result =
                await RoslynCompilerBackend.AwaitOneShotProcessCompletionAsync(
                    stdoutCompletionSource.Task,
                    stderrCompletionSource.Task,
                    exitCompletionSource.Task,
                    () => 7,
                    () => { },
                    CancellationToken.None);

            Assert.That(result.StandardOutput, Is.EqualTo("stdout"));
            Assert.That(result.StandardError, Is.EqualTo("stderr"));
            Assert.That(result.ExitCode, Is.EqualTo(7));
        }

        [Test]
        public void AwaitOneShotProcessCompletionAsync_WhenCancellationIsRequested_ShouldRequestCancellationAndThrow()
        {
            TaskCompletionSource<string> stdoutCompletionSource =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<string> stderrCompletionSource =
                new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            TaskCompletionSource<bool> exitCompletionSource =
                new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            using CancellationTokenSource cancellationTokenSource = new CancellationTokenSource();

            bool cancellationRequested = false;
            Task<RoslynCompilerBackend.OneShotProcessCompletionResult> completionTask =
                RoslynCompilerBackend.AwaitOneShotProcessCompletionAsync(
                    stdoutCompletionSource.Task,
                    stderrCompletionSource.Task,
                    exitCompletionSource.Task,
                    () => 0,
                    () =>
                    {
                        cancellationRequested = true;
                        stdoutCompletionSource.TrySetResult(string.Empty);
                        stderrCompletionSource.TrySetResult(string.Empty);
                        exitCompletionSource.TrySetResult(true);
                    },
                    cancellationTokenSource.Token);

            cancellationTokenSource.Cancel();

            Assert.That(async () => await completionTask, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(cancellationRequested, Is.True);
        }

        [Test]
        public void WriteCompilerResponseFile_WhenDefineSymbolsAndUnsafeAreProvided_ShouldEmitUnityCompilationOptions()
        {
            string responseFilePath = Path.Combine(_tempDirectoryPath, "compile.rsp");
            string sourcePath = Path.Combine(_tempDirectoryPath, "TestSource.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "TestAssembly.dll");
            List<string> references = new List<string>
            {
                Path.Combine(_tempDirectoryPath, "ReferenceA.dll"),
                Path.Combine(_tempDirectoryPath, "ReferenceB.dll")
            };

            RoslynCompilerBackend.WriteCompilerResponseFile(
                responseFilePath,
                sourcePath,
                dllPath,
                references,
                new[] { "UNITY_EDITOR", "CUSTOM_SYMBOL" },
                true);

            string[] lines = File.ReadAllLines(responseFilePath);

            Assert.That(lines, Contains.Item("-unsafe+"));
            Assert.That(lines, Contains.Item("-define:UNITY_EDITOR;CUSTOM_SYMBOL"));
            Assert.That(lines, Contains.Item($"-out:\"{dllPath}\""));
            Assert.That(lines.Count(line => line.StartsWith("-r:")), Is.EqualTo(2));
        }

        [Test]
        public void WriteWorkerRequestFile_WhenDefineSymbolsAndUnsafeAreProvided_ShouldSerializeCompilationSettings()
        {
            string requestFilePath = Path.Combine(_tempDirectoryPath, "compile.worker");
            string sourcePath = Path.Combine(_tempDirectoryPath, "TestSource.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "TestAssembly.dll");
            List<string> references = new List<string>
            {
                Path.Combine(_tempDirectoryPath, "ReferenceA.dll"),
                Path.Combine(_tempDirectoryPath, "ReferenceB.dll")
            };

            RoslynCompilerBackend.WriteWorkerRequestFile(
                requestFilePath,
                sourcePath,
                dllPath,
                references,
                new[] { "UNITY_EDITOR", "CUSTOM_SYMBOL" },
                true);

            string[] lines = File.ReadAllLines(requestFilePath);

            Assert.That(lines[0], Is.EqualTo(Path.GetFullPath(sourcePath)));
            Assert.That(lines[1], Is.EqualTo(Path.GetFullPath(dllPath)));
            Assert.That(lines, Contains.Item("unsafe:1"));
            Assert.That(lines, Contains.Item("define:UNITY_EDITOR;CUSTOM_SYMBOL"));
            Assert.That(lines, Contains.Item($"ref:{Path.GetFullPath(references[0])}"));
            Assert.That(lines, Contains.Item($"ref:{Path.GetFullPath(references[1])}"));
        }

        [Test]
        public async Task CompileAsync_WhenWorkerCompilationSucceeds_ShouldUseSharedWorkerBackend()
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available for this test");

            DynamicReferenceSetBuilderService referenceSetBuilder = new DynamicReferenceSetBuilderService();
            List<string> references = referenceSetBuilder.BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);
            string sourcePath = Path.Combine(_tempDirectoryPath, "BackendWorkerSmokeTest.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "BackendWorkerSmokeTest.dll");
            File.WriteAllText(
                sourcePath,
                "public static class BackendWorkerSmokeTest { public static int Execute() { return 5; } }");

            int buildCount = 0;
            bool buildStarted = false;
            bool buildFinished = false;

            DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileAsync(
                sourcePath,
                dllPath,
                references,
                externalCompilerPaths,
                CancellationToken.None,
                () => buildStarted = true,
                () => buildFinished = true,
                () => buildCount++);

            Assert.That(result, Is.Not.Null);
            Assert.That(result.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.SharedRoslynWorker));
            Assert.That(result.CompilerMessages.Any(message => message.type == CompilerMessageType.Error), Is.False);
            Assert.That(File.Exists(dllPath), Is.True);
            Assert.That(buildCount, Is.EqualTo(1));
            Assert.That(buildStarted, Is.True);
            Assert.That(buildFinished, Is.True);
        }
    }
}
