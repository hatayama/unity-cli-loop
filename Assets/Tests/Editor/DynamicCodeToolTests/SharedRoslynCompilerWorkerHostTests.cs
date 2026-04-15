using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine.TestTools;

namespace io.github.hatayama.uLoopMCP.DynamicCodeToolTests
{
    [TestFixture]
    public class SharedRoslynCompilerWorkerHostTests
    {
        private string _tempDirectoryPath;
        private Action<string> _previousWorkerDirectoryDeleter;
        private Action<Process, string> _previousCompileRequestSender;
        private Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]>
            _previousWorkerAssemblyCompiler;
        private bool _workerAssemblyCompilerSwapped;

        [SetUp]
        public void SetUp()
        {
            _tempDirectoryPath = Path.Combine(Path.GetTempPath(), $"SharedRoslynCompilerWorkerHostTests_{System.Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDirectoryPath);
            DynamicCompilationHealthMonitor.ResetForTests();
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
        }

        [TearDown]
        public void TearDown()
        {
            if (_previousWorkerDirectoryDeleter != null)
            {
                SharedRoslynCompilerWorkerHost.SwapWorkerDirectoryDeleterForTests(_previousWorkerDirectoryDeleter);
                _previousWorkerDirectoryDeleter = null;
            }

            if (_previousCompileRequestSender != null)
            {
                SharedRoslynCompilerWorkerHost.SwapCompileRequestSenderForTests(_previousCompileRequestSender);
                _previousCompileRequestSender = null;
            }

            if (_workerAssemblyCompilerSwapped)
            {
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(_previousWorkerAssemblyCompiler);
                _previousWorkerAssemblyCompiler = null;
                _workerAssemblyCompilerSwapped = false;
            }

            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            DynamicCompilationHealthMonitor.ResetForTests();

            if (Directory.Exists(_tempDirectoryPath))
            {
                Directory.Delete(_tempDirectoryPath, true);
            }
        }

        [Test]
        public void TryCompile_WhenExternalCompilerIsAvailable_ShouldBuildAssembly()
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available for this test");

            DynamicReferenceSetBuilderService referenceSetBuilder = new DynamicReferenceSetBuilderService();
            List<string> references = referenceSetBuilder.BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);

            string sourcePath = Path.Combine(_tempDirectoryPath, "WorkerSmokeTest.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "WorkerSmokeTest.dll");
            string requestFilePath = Path.Combine(_tempDirectoryPath, "WorkerSmokeTest.worker");

            File.WriteAllText(
                sourcePath,
                "public static class WorkerSmokeTest { public static int Execute() { return 3; } }");
            File.WriteAllLines(
                requestFilePath,
                new[] { Path.GetFullPath(sourcePath), Path.GetFullPath(dllPath) }
                    .Concat(references.Select(Path.GetFullPath)));

            int buildCount = 0;
            bool buildStarted = false;
            bool buildFinished = false;

            CompilerMessage[] messages = SharedRoslynCompilerWorkerHost.TryCompile(
                requestFilePath,
                externalCompilerPaths,
                CancellationToken.None,
                () => buildStarted = true,
                () => buildFinished = true,
                () => buildCount++);

            Assert.That(messages, Is.Not.Null, "Worker path should return compiler output instead of falling back");
            Assert.That(
                messages.Any(message => message.type == CompilerMessageType.Error),
                Is.False,
                string.Join("\n", messages.Select(message => message.message)));
            Assert.That(File.Exists(dllPath), Is.True, "Worker compilation should emit the target assembly");
            Assert.That(buildCount, Is.EqualTo(1));
            Assert.That(buildStarted, Is.True);
            Assert.That(buildFinished, Is.True);
        }

        [Test]
        public void TryCompile_WhenCompilationProducesWarning_ShouldReturnWarningWithoutFallback()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "public static class WorkerWarningTest { public static int Execute() { int unused = 1; return 3; } }",
                Array.Empty<string>(),
                false,
                out bool buildStarted,
                out bool buildFinished,
                out int buildCount,
                out string dllPath);

            Assert.That(messages, Is.Not.Null);
            Assert.That(messages.Any(message => message.type == CompilerMessageType.Warning), Is.True);
            Assert.That(File.Exists(dllPath), Is.True);
            Assert.That(buildCount, Is.EqualTo(1));
            Assert.That(buildStarted, Is.True);
            Assert.That(buildFinished, Is.True);
        }

        [Test]
        public void TryCompile_WhenCompilationProducesError_ShouldReturnErrorWithoutFallback()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "public static class WorkerErrorTest { public static int Execute() { return MissingType.Value; } }",
                Array.Empty<string>(),
                false,
                out bool buildStarted,
                out bool buildFinished,
                out int buildCount,
                out string dllPath);

            Assert.That(messages, Is.Not.Null);
            Assert.That(messages.Any(message => message.type == CompilerMessageType.Error), Is.True);
            Assert.That(File.Exists(dllPath), Is.True);
            Assert.That(buildCount, Is.EqualTo(1));
            Assert.That(buildStarted, Is.True);
            Assert.That(buildFinished, Is.True);
        }

        [Test]
        public void TryCompile_WhenWorkerStarts_ShouldUseProcessScopedWorkerDirectory()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "public static class WorkerScopedDirectoryTest { public static int Execute() { return 7; } }",
                Array.Empty<string>(),
                false,
                out bool buildStarted,
                out bool buildFinished,
                out int buildCount,
                out string dllPath);

            string workerDirectoryPath = GetWorkerDirectoryPath();

            Assert.That(messages, Is.Not.Null);
            Assert.That(File.Exists(dllPath), Is.True);
            Assert.That(Directory.Exists(workerDirectoryPath), Is.True);
            Assert.That(buildCount, Is.EqualTo(1));
            Assert.That(buildStarted, Is.True);
            Assert.That(buildFinished, Is.True);
        }

        [Test]
        public void ShutdownForTests_WhenWorkerDirectoryExists_ShouldDeleteWorkerDirectory()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "public static class WorkerDirectoryCleanupTest { public static int Execute() { return 11; } }",
                Array.Empty<string>(),
                false,
                out _,
                out _,
                out _,
                out _);

            string workerDirectoryPath = GetWorkerDirectoryPath();

            Assert.That(messages, Is.Not.Null);
            Assert.That(Directory.Exists(workerDirectoryPath), Is.True);

            SharedRoslynCompilerWorkerHost.ShutdownForTests();

            Assert.That(Directory.Exists(workerDirectoryPath), Is.False);
        }

        [Test]
        public void ShutdownForServerReset_WhenWorkerDirectoryExists_ShouldDeleteWorkerDirectory()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "public static class WorkerServerResetCleanupTest { public static int Execute() { return 13; } }",
                Array.Empty<string>(),
                false,
                out _,
                out _,
                out _,
                out _);

            string workerDirectoryPath = GetWorkerDirectoryPath();

            Assert.That(messages, Is.Not.Null);
            Assert.That(Directory.Exists(workerDirectoryPath), Is.True);

            SharedRoslynCompilerWorkerHost.ShutdownForServerReset();

            Assert.That(Directory.Exists(workerDirectoryPath), Is.False);
        }

        [Test]
        public void ShutdownForTests_WhenWorkerDirectoryDeletionFails_ShouldNotThrow()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "public static class WorkerDirectoryCleanupFailureTest { public static int Execute() { return 17; } }",
                Array.Empty<string>(),
                false,
                out _,
                out _,
                out _,
                out _);

            string workerDirectoryPath = GetWorkerDirectoryPath();

            Assert.That(messages, Is.Not.Null);
            Assert.That(Directory.Exists(workerDirectoryPath), Is.True);

            _previousWorkerDirectoryDeleter = SharedRoslynCompilerWorkerHost.SwapWorkerDirectoryDeleterForTests(
                _ => throw new IOException("locked for test"));

            Assert.That(
                () => SharedRoslynCompilerWorkerHost.ShutdownForTests(),
                Throws.Nothing);
            Assert.That(Directory.Exists(workerDirectoryPath), Is.True);

            SharedRoslynCompilerWorkerHost.SwapWorkerDirectoryDeleterForTests(_previousWorkerDirectoryDeleter);
            _previousWorkerDirectoryDeleter = null;

            Assert.That(
                () => SharedRoslynCompilerWorkerHost.ShutdownForTests(),
                Throws.Nothing);
            Assert.That(Directory.Exists(workerDirectoryPath), Is.False);
        }

        [Test]
        public void TryCompile_WhenDefineSymbolIsProvided_ShouldCompileDefinedBranch()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "#if ULOOP_TEST_DEFINE\n"
                + "public static class WorkerDefinedBranchTest { public static int Execute() { return 23; } }\n"
                + "#else\n"
                + "this will not compile\n"
                + "#endif",
                new[] { "ULOOP_TEST_DEFINE" },
                false,
                out _,
                out _,
                out _,
                out string dllPath);

            Assert.That(messages, Is.Not.Null);
            Assert.That(messages.Any(message => message.type == CompilerMessageType.Error), Is.False);
            Assert.That(File.Exists(dllPath), Is.True);
        }

        [Test]
        public void TryCompile_WhenUnsafeIsEnabled_ShouldCompileUnsafeCode()
        {
            CompilerMessage[] messages = CompileWithWorker(
                "public static unsafe class WorkerUnsafeTest { public static int Read(int* value) { return *value; } }",
                Array.Empty<string>(),
                true,
                out _,
                out _,
                out _,
                out string dllPath);

            Assert.That(messages, Is.Not.Null);
            Assert.That(messages.Any(message => message.type == CompilerMessageType.Error), Is.False);
            Assert.That(File.Exists(dllPath), Is.True);
        }

        [Test]
        public void TryCompile_WhenWorkerCommunicationThrowsIOException_ShouldReturnNullWithoutThrowing()
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available for this test");

            string sourcePath = Path.Combine(_tempDirectoryPath, "WorkerIOExceptionTest.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "WorkerIOExceptionTest.dll");
            string requestFilePath = Path.Combine(_tempDirectoryPath, "WorkerIOExceptionTest.worker");
            DynamicReferenceSetBuilderService referenceSetBuilder = new DynamicReferenceSetBuilderService();
            List<string> references = referenceSetBuilder.BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);

            File.WriteAllText(
                sourcePath,
                "public static class WorkerIOExceptionTest { public static int Execute() { return 29; } }");
            File.WriteAllLines(
                requestFilePath,
                new[] { Path.GetFullPath(sourcePath), Path.GetFullPath(dllPath) }
                    .Concat(references.Select(Path.GetFullPath)));

            int buildCount = 0;
            bool buildStarted = false;
            bool buildFinished = false;
            _previousCompileRequestSender = SharedRoslynCompilerWorkerHost.SwapCompileRequestSenderForTests(
                (_, _) => throw new IOException("worker stream broken"));
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("execute-dynamic-code shared Roslyn worker failed to operate correctly"));

            CompilerMessage[] messages = SharedRoslynCompilerWorkerHost.TryCompile(
                requestFilePath,
                externalCompilerPaths,
                CancellationToken.None,
                () => buildStarted = true,
                () => buildFinished = true,
                () => buildCount++);

            Assert.That(messages, Is.Null);
            Assert.That(buildCount, Is.EqualTo(2));
            Assert.That(buildStarted, Is.True);
            Assert.That(buildFinished, Is.True);
        }

        [Test]
        public void TryCompile_WhenWorkerAssemblyBuildLeavesStaleDll_ShouldRebuildOnRetry()
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available for this test");

            string sourcePath = Path.Combine(_tempDirectoryPath, "WorkerRebuildRetryTest.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "WorkerRebuildRetryTest.dll");
            string requestFilePath = Path.Combine(_tempDirectoryPath, "WorkerRebuildRetryTest.worker");
            DynamicReferenceSetBuilderService referenceSetBuilder = new DynamicReferenceSetBuilderService();
            List<string> references = referenceSetBuilder.BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);

            File.WriteAllText(
                sourcePath,
                "public static class WorkerRebuildRetryTest { public static int Execute() { return 31; } }");
            File.WriteAllLines(
                requestFilePath,
                new[] { Path.GetFullPath(sourcePath), Path.GetFullPath(dllPath) }
                    .Concat(references.Select(Path.GetFullPath)));

            int workerAssemblyBuildCount = 0;
            _previousWorkerAssemblyCompiler = SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(
                (_, _, workerAssemblyPath, _) =>
                {
                    workerAssemblyBuildCount++;
                    File.WriteAllText(workerAssemblyPath, "invalid worker assembly");
                    return new[]
                    {
                        new CompilerMessage
                        {
                            type = CompilerMessageType.Error,
                            message = "simulated worker assembly build failure"
                        }
                    };
                });
            _workerAssemblyCompilerSwapped = true;
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex("execute-dynamic-code shared Roslyn worker failed to operate correctly"));

            CompilerMessage[] messages = SharedRoslynCompilerWorkerHost.TryCompile(
                requestFilePath,
                externalCompilerPaths,
                CancellationToken.None,
                () => { },
                () => { },
                () => { });

            Assert.That(messages, Is.Null);
            Assert.That(workerAssemblyBuildCount, Is.EqualTo(2));
            Assert.That(File.Exists(GetWorkerAssemblyPath()), Is.False);
        }

        private CompilerMessage[] CompileWithWorker(
            string source,
            IReadOnlyCollection<string> defineSymbols,
            bool allowUnsafeCode,
            out bool buildStarted,
            out bool buildFinished,
            out int buildCount,
            out string dllPath)
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available for this test");

            DynamicReferenceSetBuilderService referenceSetBuilder = new DynamicReferenceSetBuilderService();
            List<string> references = referenceSetBuilder.BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);

            string sourcePath = Path.Combine(_tempDirectoryPath, $"{System.Guid.NewGuid():N}.cs");
            dllPath = Path.Combine(_tempDirectoryPath, $"{System.Guid.NewGuid():N}.dll");
            string requestFilePath = Path.Combine(_tempDirectoryPath, $"{System.Guid.NewGuid():N}.worker");

            File.WriteAllText(sourcePath, source);
            RoslynCompilerBackend.WriteWorkerRequestFile(
                requestFilePath,
                sourcePath,
                dllPath,
                references,
                defineSymbols,
                allowUnsafeCode);

            int localBuildCount = 0;
            bool localBuildStarted = false;
            bool localBuildFinished = false;

            CompilerMessage[] messages = SharedRoslynCompilerWorkerHost.TryCompile(
                requestFilePath,
                externalCompilerPaths,
                CancellationToken.None,
                () => localBuildStarted = true,
                () => localBuildFinished = true,
                () => localBuildCount++);

            buildCount = localBuildCount;
            buildStarted = localBuildStarted;
            buildFinished = localBuildFinished;
            return messages;
        }

        private static string GetWorkerDirectoryPath()
        {
            return Path.Combine(
                Path.GetTempPath(),
                "uLoopMCPCompilation",
                $"RoslynWorker-{Process.GetCurrentProcess().Id}");
        }

        private static string GetWorkerAssemblyPath()
        {
            return Path.Combine(GetWorkerDirectoryPath(), "RoslynCompilerWorker.dll");
        }
    }
}
