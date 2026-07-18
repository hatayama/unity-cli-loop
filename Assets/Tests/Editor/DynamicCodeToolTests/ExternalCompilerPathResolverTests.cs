using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEditor.Compilation;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Test fixture that verifies External Compiler Path Resolver behavior.
    /// </summary>
    [TestFixture]
    public class ExternalCompilerPathResolverTests
    {
        private const int FallbackCompileTimeoutMilliseconds = 30000;

        private string _tempDirectoryPath;

        [SetUp]
        public void SetUp()
        {
            _tempDirectoryPath = Path.Combine(Path.GetTempPath(), $"ExternalCompilerPathResolverTests_{System.Guid.NewGuid():N}");
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
        public void ResolveNetCoreRuntimeSharedDirectoryPath_WhenMultipleRuntimeVersionsExist_ShouldChooseHighestVersion()
        {
            string runtimeRootPath = CreateDirectory("Microsoft.NETCore.App");
            string olderRuntimeDirectoryPath = CreateDirectory(Path.Combine("Microsoft.NETCore.App", "8.0.0"));
            string latestRuntimeDirectoryPath = CreateDirectory(Path.Combine("Microsoft.NETCore.App", "8.0.14"));
            CreateDirectory(Path.Combine("Microsoft.NETCore.App", "7.0.20"));

            string resolvedDirectoryPath = ExternalCompilerPathResolver.ResolveNetCoreRuntimeSharedDirectoryPath(runtimeRootPath);

            Assert.That(resolvedDirectoryPath, Is.EqualTo(latestRuntimeDirectoryPath));
            Assert.That(resolvedDirectoryPath, Is.Not.EqualTo(olderRuntimeDirectoryPath));
        }

        [Test]
        public void ResolveNetCoreRuntimeSharedDirectoryPath_WhenVersionAndNonVersionDirectoriesExist_ShouldPreferHighestVersion()
        {
            string runtimeRootPath = CreateDirectory("Microsoft.NETCore.App");
            CreateDirectory(Path.Combine("Microsoft.NETCore.App", "current"));
            string latestRuntimeDirectoryPath = CreateDirectory(Path.Combine("Microsoft.NETCore.App", "9.0.1"));

            string resolvedDirectoryPath = ExternalCompilerPathResolver.ResolveNetCoreRuntimeSharedDirectoryPath(runtimeRootPath);

            Assert.That(resolvedDirectoryPath, Is.EqualTo(latestRuntimeDirectoryPath));
        }

        [Test]
        public void ResolveNetCoreRuntimeSharedDirectoryPath_WhenOnlyNonVersionDirectoriesExist_ShouldChooseDeterministicDirectory()
        {
            string runtimeRootPath = CreateDirectory("Microsoft.NETCore.App");
            CreateDirectory(Path.Combine("Microsoft.NETCore.App", "alpha"));
            string expectedDirectoryPath = CreateDirectory(Path.Combine("Microsoft.NETCore.App", "release"));

            string resolvedDirectoryPath = ExternalCompilerPathResolver.ResolveNetCoreRuntimeSharedDirectoryPath(runtimeRootPath);

            Assert.That(resolvedDirectoryPath, Is.EqualTo(expectedDirectoryPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenLegacyLayoutExists_ShouldReturnContentsPath()
        {
            string contentsPath = CreateDirectory("Contents");
            CreateDirectory(Path.Combine("Contents", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(contentsPath));
        }

        [Test]
        public void ResolveCompilerLayoutKind_WhenContentsRootLegacyRoslynLayoutExists_ShouldReturnContentsRootDotNetSdkRoslyn()
        {
            // Verifies Unity 2022-style compiler roots are classified as legacy contents-root Roslyn.
            string contentsPath = CreateDirectory("Contents");
            string compilerDirectoryPath = CreateDirectory(Path.Combine("Contents", "DotNetSdkRoslyn"));

            ExternalCompilerLayoutKind layoutKind = ExternalCompilerPathResolver.ResolveCompilerLayoutKind(
                contentsPath,
                contentsPath,
                compilerDirectoryPath);

            Assert.That(layoutKind, Is.EqualTo(ExternalCompilerLayoutKind.ContentsRootDotNetSdkRoslyn));
        }

        [Test]
        public void ResolveCompilerLayoutKind_WhenResourcesScriptingRoslynLayoutExists_ShouldReturnResourcesScripting()
        {
            // Verifies Unity 6-style Resources/Scripting compiler roots stay on the current shared-worker path.
            string contentsPath = CreateDirectory("Contents");
            string scriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            string compilerDirectoryPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn"));

            ExternalCompilerLayoutKind layoutKind = ExternalCompilerPathResolver.ResolveCompilerLayoutKind(
                contentsPath,
                scriptingRootPath,
                compilerDirectoryPath);

            Assert.That(layoutKind, Is.EqualTo(ExternalCompilerLayoutKind.ResourcesScripting));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenResourcesScriptingLayoutExists_ShouldReturnResourcesScriptingPath()
        {
            // Verifies Unity's Resources/Scripting compiler layout is preferred when present.
            string contentsPath = CreateDirectory("Contents");
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenResourcesScriptingDotNetSdkLayoutExists_ShouldReturnResourcesScriptingPath()
        {
            // Verifies Unity 6.5 DotNetSdk compiler layouts are accepted under Resources/Scripting.
            string contentsPath = CreateDirectory("Contents");
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenBothLayoutsExist_ShouldPreferResourcesScriptingLayout()
        {
            // Verifies the current Resources/Scripting layout wins over the legacy contents-root layout.
            string contentsPath = CreateDirectory("Contents");
            CreateDirectory(Path.Combine("Contents", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "DotNetSdkRoslyn", "csc.dll"));
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "Resources", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "Resources", "Scripting", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveScriptingRootPath_WhenKnownLayoutsAreMissing_ShouldDiscoverNestedCompilerLayout()
        {
            string contentsPath = CreateDirectory("Contents");
            string expectedScriptingRootPath = CreateDirectory(Path.Combine("Contents", "PlaybackEngines", "Custom", "Scripting"));
            CreateDirectory(Path.Combine("Contents", "PlaybackEngines", "Custom", "Scripting", "NetCoreRuntime"));
            CreateDirectory(Path.Combine("Contents", "PlaybackEngines", "Custom", "Scripting", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Contents", "PlaybackEngines", "Custom", "Scripting", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedScriptingRootPath = ExternalCompilerPathResolver.ResolveScriptingRootPath(contentsPath);

            Assert.That(resolvedScriptingRootPath, Is.EqualTo(expectedScriptingRootPath));
        }

        [Test]
        public void ResolveCompilerDirectoryPath_WhenLegacyLayoutExists_ShouldReturnDotNetSdkRoslynPath()
        {
            // Verifies legacy compiler roots keep resolving to DotNetSdkRoslyn.
            string scriptingRootPath = CreateDirectory("Scripting");
            string expectedCompilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdkRoslyn"));
            CreateFile(Path.Combine("Scripting", "DotNetSdkRoslyn", "csc.dll"));

            string resolvedCompilerDirectoryPath = ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath);

            Assert.That(resolvedCompilerDirectoryPath, Is.EqualTo(expectedCompilerDirectoryPath));
        }

        [Test]
        public void ResolveCompilerDirectoryPath_WhenLegacyLayoutIsIncomplete_ShouldUseDotNetSdkLayout()
        {
            // Verifies stale legacy compiler roots fall back to the versioned DotNetSdk layout.
            string scriptingRootPath = CreateDirectory("Scripting");
            CreateDirectory(Path.Combine("Scripting", "DotNetSdkRoslyn"));
            string expectedCompilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore"));

            string resolvedCompilerDirectoryPath = ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath);

            Assert.That(resolvedCompilerDirectoryPath, Is.EqualTo(expectedCompilerDirectoryPath));
        }

        [Test]
        public void ResolveCompilerDirectoryPath_WhenDotNetSdkLayoutHasMultipleSdkVersions_ShouldChooseHighestSdkRoslynBincorePath()
        {
            // Verifies Unity 6.5 SDK layouts choose the newest versioned Roslyn compiler directory.
            string scriptingRootPath = CreateDirectory("Scripting");
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "8.0.100", "Roslyn", "bincore"));
            string expectedCompilerDirectoryPath = CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "8.0.318", "Roslyn", "bincore"));
            CreateDirectory(Path.Combine("Scripting", "DotNetSdk", "sdk", "current", "Roslyn", "bincore"));

            string resolvedCompilerDirectoryPath = ExternalCompilerPathResolver.ResolveCompilerDirectoryPath(scriptingRootPath);

            Assert.That(resolvedCompilerDirectoryPath, Is.EqualTo(expectedCompilerDirectoryPath));
        }

        [Test]
        public async Task CompileAsync_WhenSharedWorkerBuildFails_ShouldFallbackToOneShotRoslyn()
        {
            // Verifies a broken worker cannot disable execute-dynamic-code; the bounded fallback still completes.
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available.");

            string sourcePath = Path.Combine(_tempDirectoryPath, "WorkerFallbackSmoke.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "WorkerFallbackSmoke.dll");
            File.WriteAllText(
                sourcePath,
                "public static class WorkerFallbackSmoke { public static int Execute() { return 7; } }");
            DynamicReferenceSetBuilderService referenceSetBuilder = new DynamicReferenceSetBuilderService();
            List<string> references = referenceSetBuilder.BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);
            int buildCount = 0;
            bool buildStarted = false;
            bool buildFinished = false;

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

            Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> previousWorkerCompiler =
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(
                    (ExternalCompilerPaths paths, string workerSourcePath, string workerAssemblyPath, string workerCompileResponseFilePath) =>
                        new CompilerMessage[]
                        {
                            new CompilerMessage
                            {
                                type = CompilerMessageType.Error,
                                message = "synthetic worker build failure"
                            }
                        });

            try
            {
                using CancellationTokenSource compileCancellationTokenSource = new CancellationTokenSource();
                compileCancellationTokenSource.CancelAfter(FallbackCompileTimeoutMilliseconds);
                DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileAsync(
                    sourcePath,
                    dllPath,
                    references,
                    externalCompilerPaths,
                    new RoslynCompilerOptions(Array.Empty<string>(), false),
                    compileCancellationTokenSource.Token,
                    () => buildStarted = true,
                    () => buildFinished = true,
                    () => buildCount++);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.OneShotRoslyn));
                Assert.That(File.Exists(dllPath), Is.True);
                Assert.That(buildCount, Is.EqualTo(1));
                Assert.That(buildStarted, Is.True);
                Assert.That(buildFinished, Is.True);
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(previousWorkerCompiler);
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
            }
        }

        /// <summary>
        /// Verifies a lifecycle transition during worker startup silently falls back to one-shot Roslyn.
        /// </summary>
        [Test]
        public async Task CompileAsync_WhenWorkerLifecycleClosesDuringStartup_ShouldFallbackWithoutErrorLogs()
        {
            ExternalCompilerPaths externalCompilerPaths = ExternalCompilerPathResolver.Resolve();
            Assert.That(externalCompilerPaths, Is.Not.Null, "Unity external compiler layout should be available.");

            string sourcePath = Path.Combine(_tempDirectoryPath, "LifecycleFallbackSmoke.cs");
            string dllPath = Path.Combine(_tempDirectoryPath, "LifecycleFallbackSmoke.dll");
            File.WriteAllText(
                sourcePath,
                "public static class LifecycleFallbackSmoke { public static int Execute() { return 11; } }");
            List<string> references = new DynamicReferenceSetBuilderService().BuildReferenceSet(
                new List<string>(),
                null,
                externalCompilerPaths);
            int buildCount = 0;
            bool buildStarted = false;
            bool buildFinished = false;

            DynamicCompilationHealthMonitor.ResetForTests();
            SharedRoslynCompilerWorkerHost.ShutdownForTests();
            LogAssert.NoUnexpectedReceived();

            Func<ExternalCompilerPaths, string, string, string, CompilerMessage[]> previousWorkerCompiler =
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(
                    (ExternalCompilerPaths paths, string workerSourcePath, string workerAssemblyPath, string workerCompileResponseFilePath) =>
                    {
                        SharedRoslynCompilerWorkerHost.ShutdownForTests();
                        return Array.Empty<CompilerMessage>();
                    });

            try
            {
                using CancellationTokenSource compileCancellationTokenSource = new CancellationTokenSource();
                compileCancellationTokenSource.CancelAfter(FallbackCompileTimeoutMilliseconds);
                DynamicCompilationBackendResult result = await RoslynCompilerBackend.CompileAsync(
                    sourcePath,
                    dllPath,
                    references,
                    externalCompilerPaths,
                    new RoslynCompilerOptions(Array.Empty<string>(), false),
                    compileCancellationTokenSource.Token,
                    () => buildStarted = true,
                    () => buildFinished = true,
                    () => buildCount++);

                Assert.That(result, Is.Not.Null);
                Assert.That(result.BackendKind, Is.EqualTo(DynamicCompilationBackendKind.OneShotRoslyn));
                Assert.That(File.Exists(dllPath), Is.True);
                Assert.That(buildCount, Is.EqualTo(1));
                Assert.That(buildStarted, Is.True);
                Assert.That(buildFinished, Is.True);
                LogAssert.NoUnexpectedReceived();
            }
            finally
            {
                SharedRoslynCompilerWorkerHost.SwapWorkerAssemblyCompilerForTests(previousWorkerCompiler);
                SharedRoslynCompilerWorkerHost.ShutdownForTests();
            }
        }

        [Test]
        public void ReportInfrastructureFallback_WhenCompilerPathLayoutIsKnown_ShouldIncludeLayoutKind()
        {
            DynamicCompilationHealthMonitor.ResetForTests();
            ExternalCompilerPaths externalCompilerPaths = CreateExternalCompilerPaths(ExternalCompilerLayoutKind.Scanned);
            LogAssert.Expect(
                UnityEngine.LogType.Error,
                new System.Text.RegularExpressions.Regex(
                    "dynamic_code_one_shot_compiler_start_failure[\\s\\S]*layout_kind = Scanned"));

            RoslynCompilerBackend.ReportInfrastructureFallback(externalCompilerPaths, 57);
        }

        private string CreateDirectory(string relativePath)
        {
            string directoryPath = Path.Combine(_tempDirectoryPath, relativePath);
            Directory.CreateDirectory(directoryPath);
            return directoryPath;
        }

        private string CreateFile(string relativePath)
        {
            string filePath = Path.Combine(_tempDirectoryPath, relativePath);
            Directory.CreateDirectory(Path.GetDirectoryName(filePath));
            File.WriteAllText(filePath, string.Empty);
            return filePath;
        }

        private ExternalCompilerPaths CreateExternalCompilerPaths(ExternalCompilerLayoutKind layoutKind)
        {
            string editorContentsPath = CreateDirectory("EditorContents");
            string scriptingRootPath = CreateDirectory("Scripting");
            string dotnetHostPath = CreateFile(Path.Combine("DotNet", "dotnet"));
            string compilerDllPath = CreateFile(Path.Combine("Roslyn", "csc.dll"));
            string compilerRuntimeConfigPath = CreateFile(Path.Combine("Roslyn", "csc.runtimeconfig.json"));
            string compilerDepsFilePath = CreateFile(Path.Combine("Roslyn", "csc.deps.json"));
            string codeAnalysisDllPath = CreateFile(Path.Combine("Roslyn", "Microsoft.CodeAnalysis.dll"));
            string codeAnalysisCSharpDllPath = CreateFile(Path.Combine("Roslyn", "Microsoft.CodeAnalysis.CSharp.dll"));
            string netCoreRuntimeSharedDirectoryPath = CreateDirectory(Path.Combine("Runtime", "shared"));

            return new ExternalCompilerPaths(
                editorContentsPath,
                scriptingRootPath,
                dotnetHostPath,
                compilerDllPath,
                compilerRuntimeConfigPath,
                compilerDepsFilePath,
                codeAnalysisDllPath,
                codeAnalysisCSharpDllPath,
                netCoreRuntimeSharedDirectoryPath,
                layoutKind);
        }

    }
}
