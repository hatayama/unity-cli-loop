using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Guards one-shot csc response-file options that enable portable PDB emission.
    /// </summary>
    [TestFixture]
    public sealed class RoslynCompilerBackendTests
    {
        /// <summary>
        /// Verifies WriteCompilerResponseFile emits -debug:portable so the one-shot csc path keeps PDBs.
        /// </summary>
        [Test]
        public void WriteCompilerResponseFile_IncludesPortableDebugOption()
        {
            string responseFilePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-rsp-" + Path.GetRandomFileName());
            try
            {
                RoslynCompilerRequestFileWriter.WriteCompilerResponseFile(
                    responseFilePath,
                    sourcePath: "snippet.cs",
                    dllPath: "snippet.dll",
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: false);

                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain("-debug:portable"));
                Assert.That(lines, Does.Not.Contain("-debug-"));
                Assert.That(lines, Does.Contain("-optimize+"));
            }
            finally
            {
                if (File.Exists(responseFilePath))
                {
                    File.Delete(responseFilePath);
                }
            }
        }

        /// <summary>
        /// WriteCompilerResponseFile requests English diagnostics and UTF-8 compiler output.
        /// </summary>
        [Test]
        public void WriteCompilerResponseFile_IncludesEnglishDiagnosticsAndUtf8OutputOptions()
        {
            string responseFilePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-rsp-" + Path.GetRandomFileName());
            try
            {
                RoslynCompilerRequestFileWriter.WriteCompilerResponseFile(
                    responseFilePath,
                    sourcePath: "snippet.cs",
                    dllPath: "snippet.dll",
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: false);

                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain("-preferreduilang:en-US"));
                Assert.That(lines, Does.Contain("-utf8output"));
            }
            finally
            {
                if (File.Exists(responseFilePath))
                {
                    File.Delete(responseFilePath);
                }
            }
        }

        /// <summary>
        /// emitDebugCode writes -optimize- so hot-reload shim one-shot fallback keeps locals.
        /// </summary>
        [Test]
        public void WriteCompilerResponseFile_WithEmitDebugCode_DisablesOptimization()
        {
            string responseFilePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-rsp-" + Path.GetRandomFileName());
            try
            {
                RoslynCompilerRequestFileWriter.WriteCompilerResponseFile(
                    responseFilePath,
                    sourcePath: "snippet.cs",
                    dllPath: "snippet.dll",
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: true);

                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain("-optimize-"));
                Assert.That(lines, Does.Not.Contain("-optimize+"));
            }
            finally
            {
                if (File.Exists(responseFilePath))
                {
                    File.Delete(responseFilePath);
                }
            }
        }

        /// <summary>
        /// WriteCompilerResponseFile maps the source directory onto the project root via -pathmap.
        /// </summary>
        [Test]
        public void WriteCompilerResponseFile_MapsSourceDirectoryToProjectRoot()
        {
            string tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-roslyn-pathmap-" + Path.GetRandomFileName());
            Directory.CreateDirectory(tempDirectory);
            string responseFilePath = Path.Combine(tempDirectory, "snippet.rsp");
            string sourcePath = Path.Combine(tempDirectory, "snippet.cs");
            string dllPath = Path.Combine(tempDirectory, "snippet.dll");
            try
            {
                RoslynCompilerRequestFileWriter.WriteCompilerResponseFile(
                    responseFilePath,
                    sourcePath,
                    dllPath,
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: false);

                string sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath));
                string expectedPathmap = "-pathmap:\"" + sourceDirectory + "=" + UnityCliLoopPathResolver.GetProjectRoot() + "\"";
                string[] lines = File.ReadAllLines(responseFilePath);
                Assert.That(lines, Does.Contain(expectedPathmap));
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        /// <summary>
        /// WriteWorkerRequestFile encodes emitDebugCode for the shared Roslyn worker.
        /// </summary>
        [Test]
        public void WriteWorkerRequestFile_IncludesDebugCodeFlag()
        {
            string requestFilePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-worker-" + Path.GetRandomFileName());
            string sourcePath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-src-" + Path.GetRandomFileName() + ".cs");
            string dllPath = Path.Combine(Path.GetTempPath(), "uloop-roslyn-dll-" + Path.GetRandomFileName() + ".dll");
            File.WriteAllText(sourcePath, "class C {}");
            try
            {
                RoslynCompilerRequestFileWriter.WriteWorkerRequestFile(
                    requestFilePath,
                    sourcePath,
                    dllPath,
                    references: new List<string>(),
                    defineSymbols: new List<string>(),
                    allowUnsafeCode: false,
                    emitDebugCode: true);

                string[] lines = File.ReadAllLines(requestFilePath);
                Assert.That(lines, Does.Contain("debugCode:1"));
                Assert.That(lines, Does.Contain("unsafe:0"));
            }
            finally
            {
                if (File.Exists(requestFilePath))
                {
                    File.Delete(requestFilePath);
                }

                if (File.Exists(sourcePath))
                {
                    File.Delete(sourcePath);
                }
            }
        }

        /// <summary>
        /// Verifies that a shared and one-shot infrastructure failure reaches AssemblyBuilder for
        /// the preserved single-source API but never for the multi-source API.
        /// </summary>
        [Test]
        public async Task CompileMultipleSourcesAsync_InfrastructureFailure_DoesNotInvokeFallback()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string directory = Path.Combine(Path.GetTempPath(), "roslyn-multiple-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            string firstSourcePath = Path.Combine(directory, "first.cs");
            string secondSourcePath = Path.Combine(directory, "second.cs");
            string dllPath = Path.Combine(directory, "output.dll");
            File.WriteAllText(firstSourcePath, "public class First { }");
            File.WriteAllText(secondSourcePath, "public class Second { }");
            int fallbackCalls = 0;
            Func<ExternalCompilerPaths, string, string, string, UnityEditor.Compilation.CompilerMessage[]> previousWorker =
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(
                    (ExternalCompilerPaths _, string __, string ___, string ____) =>
                        new[]
                        {
                            new UnityEditor.Compilation.CompilerMessage
                            {
                                type = UnityEditor.Compilation.CompilerMessageType.Error,
                                message = "worker unavailable"
                            }
                        });
            Func<System.Diagnostics.ProcessStartInfo, System.Diagnostics.Process> previousStarter =
                RoslynCompilerBackend.SwapOneShotProcessStarterForTests(_ => null);
            Func<string, string, List<string>, CancellationToken, Action, Action, Action, Task<DynamicCompilationBackendResult>> previousFallback =
                AssemblyBuilderFallbackCompilerBackend.SwapCompilerForTests(
                    (string _, string __, List<string> ___, CancellationToken ____, Action _____, Action ______, Action _______) =>
                    {
                        fallbackCalls++;
                        return Task.FromResult(
                            new DynamicCompilationBackendResult(
                                System.Array.Empty<UnityEditor.Compilation.CompilerMessage>(),
                                DynamicCompilationBackendKind.AssemblyBuilderFallback));
                    });

            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            DynamicCompilationHealthMonitor.ResetForTests();
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "execute-dynamic-code shared Roslyn worker failed to operate correctly; reason=worker_build_failed"));
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "execute-dynamic-code shared Roslyn worker is unavailable; falling back to one-shot compiler execution; reason=worker_unavailable"));
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "execute-dynamic-code one-shot Roslyn compiler process failed to start"));
            try
            {
                DynamicCompilationBackendResult single = await RoslynCompilerBackend.CompileAsync(
                    firstSourcePath,
                    dllPath,
                    new List<string>(),
                    paths,
                    new RoslynCompilerOptions(System.Array.Empty<string>(), false, false),
                    CancellationToken.None,
                    () => { },
                    () => { },
                    () => { });
                DynamicCompilationBackendResult multiple = await RoslynCompilerBackend.CompileMultipleSourcesAsync(
                    new[] { firstSourcePath, secondSourcePath },
                    dllPath,
                    new List<string>(),
                    paths,
                    new RoslynCompilerOptions(System.Array.Empty<string>(), false, false),
                    CancellationToken.None,
                    () => { },
                    () => { },
                    () => { });

                Assert.That(single.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.AssemblyBuilderFallback));
                Assert.That(multiple.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.Unknown));
                Assert.That(FindError(multiple.CompilerMessages).HasValue, Is.True);
                Assert.That(fallbackCalls, Is.EqualTo(1));
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
                AssemblyBuilderFallbackCompilerBackend.SwapCompilerForTests(previousFallback);
                RoslynCompilerBackend.SwapOneShotProcessStarterForTests(previousStarter);
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(previousWorker);
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies that the real shared Roslyn worker compiles independent UTF-8 CRLF sources
        /// whose directory and file names contain spaces.
        /// </summary>
        [Test]
        public async Task CompileMultipleSourcesAsync_RealSharedWorker_CompilesUtf8CrLfSpacePaths()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string directory = Path.Combine(Path.GetTempPath(), "roslyn sources 日本語 " + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            string firstSourcePath = Path.Combine(directory, "First Source.cs");
            string secondSourcePath = Path.Combine(directory, "Second 日本語.cs");
            string dllPath = Path.Combine(directory, "output.dll");
            File.WriteAllText(firstSourcePath, "public class SharedFirst { }\r\n");
            File.WriteAllText(
                secondSourcePath,
                "public class SharedSecond { public string Value() { return \"日本語\"; } }\r\n");
            List<string> references = new DynamicReferenceSetBuilderService().BuildReferenceSet(
                new List<string>(),
                null,
                paths);

            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            try
            {
                DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileMultipleSourcesAsync(
                    new[] { firstSourcePath, secondSourcePath },
                    dllPath,
                    references,
                    paths,
                    new RoslynCompilerOptions(Array.Empty<string>(), false, emitDebugCode: false),
                    CancellationToken.None,
                    () => { },
                    () => { },
                    () => { });

                Assert.That(result.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.SharedRoslynWorker));
                Assert.That(result.CompilerMessages, Is.Empty);
                Assert.That(File.Exists(dllPath), Is.True);
                Assert.That(File.Exists(Path.ChangeExtension(dllPath, ".pdb")), Is.True);
                Assembly assembly = Assembly.Load(File.ReadAllBytes(dllPath));
                Assert.That(assembly.GetType("SharedFirst"), Is.Not.Null);
                Assert.That(assembly.GetType("SharedSecond"), Is.Not.Null);
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies that a real one-shot compiler process handles multiple source trees when the
        /// shared worker build is unavailable.
        /// </summary>
        [Test]
        public async Task CompileMultipleSourcesAsync_WorkerBuildFailure_UsesRealOneShotRoslyn()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string directory = Path.Combine(Path.GetTempPath(), "roslyn multiple one-shot 日本語 " + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            string firstSourcePath = Path.Combine(directory, "First Source.cs");
            string secondSourcePath = Path.Combine(directory, "Second 日本語.cs");
            string dllPath = Path.Combine(directory, "output.dll");
            File.WriteAllText(firstSourcePath, "public class OneShotFirst { }\r\n");
            File.WriteAllText(
                secondSourcePath,
                "public class OneShotSecond { public string Value() { return \"日本語\"; } }\r\n");
            List<string> references = new DynamicReferenceSetBuilderService().BuildReferenceSet(
                new List<string>(),
                null,
                paths);
            Func<ExternalCompilerPaths, string, string, string, UnityEditor.Compilation.CompilerMessage[]> previousWorker =
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(
                    (ExternalCompilerPaths _, string __, string ___, string ____) =>
                        new[]
                        {
                            new UnityEditor.Compilation.CompilerMessage
                            {
                                type = UnityEditor.Compilation.CompilerMessageType.Error,
                                message = "worker unavailable"
                            }
                        });

            DynamicCompilationHealthMonitor.ResetForTests();
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "execute-dynamic-code shared Roslyn worker failed to operate correctly; reason=worker_build_failed"));
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "execute-dynamic-code shared Roslyn worker is unavailable; falling back to one-shot compiler execution; reason=worker_unavailable"));
            try
            {
                DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileMultipleSourcesAsync(
                    new[] { firstSourcePath, secondSourcePath },
                    dllPath,
                    references,
                    paths,
                    new RoslynCompilerOptions(Array.Empty<string>(), false, emitDebugCode: false),
                    CancellationToken.None,
                    () => { },
                    () => { },
                    () => { });

                Assert.That(result.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.OneShotRoslyn));
                Assert.That(result.CompilerMessages, Is.Empty);
                Assert.That(File.Exists(Path.ChangeExtension(dllPath, ".pdb")), Is.True);
                Assembly assembly = Assembly.Load(File.ReadAllBytes(dllPath));
                Assert.That(assembly.GetType("OneShotFirst"), Is.Not.Null);
                Assert.That(assembly.GetType("OneShotSecond"), Is.Not.Null);
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(previousWorker);
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        /// <summary>
        /// Verifies that the shared worker returns an error diagnostic attributed to the second source tree.
        /// </summary>
        [Test]
        public async Task CompileMultipleSourcesAsync_SecondSourceError_ReportsSecondSourceFromSharedWorker()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string directory = Path.Combine(Path.GetTempPath(), "roslyn-multiple-shared-error-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            string firstSourcePath = Path.Combine(directory, "first.cs");
            string secondSourcePath = Path.Combine(directory, "second.cs");
            string dllPath = Path.Combine(directory, "output.dll");
            File.WriteAllText(firstSourcePath, "public class ValidFirst { }");
            File.WriteAllText(secondSourcePath, "public class BrokenSecond { public MissingType Value; }");
            List<string> references = new DynamicReferenceSetBuilderService().BuildReferenceSet(new List<string>(), null, paths);
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            try
            {
                DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileMultipleSourcesAsync(
                    new[] { firstSourcePath, secondSourcePath }, dllPath, references, paths,
                    new RoslynCompilerOptions(Array.Empty<string>(), false, emitDebugCode: false),
                    CancellationToken.None, () => { }, () => { }, () => { });
                UnityEditor.Compilation.CompilerMessage? error = FindError(result.CompilerMessages);

                Assert.That(result.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.SharedRoslynWorker));
                Assert.That(error.HasValue, Is.True);
                Assert.That(error.Value.file, Is.EqualTo(secondSourcePath));
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
                Directory.Delete(directory, recursive: true);
            }
        }

        /// <summary>
        /// Verifies that the one-shot fallback returns an error diagnostic attributed to the second source tree.
        /// </summary>
        [Test]
        public async Task CompileMultipleSourcesAsync_SecondSourceError_ReportsSecondSourceFromOneShotRoslyn()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null);
            string directory = Path.Combine(Path.GetTempPath(), "roslyn-multiple-oneshot-error-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            string firstSourcePath = Path.Combine(directory, "first.cs");
            string secondSourcePath = Path.Combine(directory, "second.cs");
            string dllPath = Path.Combine(directory, "output.dll");
            File.WriteAllText(firstSourcePath, "public class ValidFirst { }");
            File.WriteAllText(secondSourcePath, "public class BrokenSecond { public MissingType Value; }");
            List<string> references = new DynamicReferenceSetBuilderService().BuildReferenceSet(new List<string>(), null, paths);
            Func<ExternalCompilerPaths, string, string, string, UnityEditor.Compilation.CompilerMessage[]> previousWorker =
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(
                    (ExternalCompilerPaths _, string __, string ___, string ____) =>
                        new[] { new UnityEditor.Compilation.CompilerMessage { type = UnityEditor.Compilation.CompilerMessageType.Error, message = "worker unavailable" } });
            DynamicCompilationHealthMonitor.ResetForTests();
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("reason=worker_build_failed"));
            LogAssert.Expect(UnityEngine.LogType.Error, new System.Text.RegularExpressions.Regex("falling back to one-shot"));
            try
            {
                DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileMultipleSourcesAsync(
                    new[] { firstSourcePath, secondSourcePath }, dllPath, references, paths,
                    new RoslynCompilerOptions(Array.Empty<string>(), false, emitDebugCode: false),
                    CancellationToken.None, () => { }, () => { }, () => { });
                UnityEditor.Compilation.CompilerMessage? error = FindError(result.CompilerMessages);

                Assert.That(result.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.OneShotRoslyn));
                Assert.That(error.HasValue, Is.True);
                Assert.That(error.Value.file, Is.EqualTo(secondSourcePath));
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(previousWorker);
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
                Directory.Delete(directory, recursive: true);
            }
        }

        private static UnityEditor.Compilation.CompilerMessage? FindError(
            UnityEditor.Compilation.CompilerMessage[] messages)
        {
            foreach (UnityEditor.Compilation.CompilerMessage message in messages)
            {
                if (message.type == UnityEditor.Compilation.CompilerMessageType.Error)
                {
                    return message;
                }
            }

            return null;
        }
    }
}
