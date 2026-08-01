using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReloadSpike
{
    /// <summary>
    /// Spike S2 for hot reload: proves that a standalone worker executable can be built with the
    /// Unity-bundled Roslyn compiler (csc.dll), run on the Unity-bundled .NET host, and use the
    /// Unity-bundled Microsoft.CodeAnalysis assemblies to parse C# source. The production
    /// transform worker relies on exactly this bootstrap: compile once with csc against the
    /// bundled shared framework, resolve Roslyn assemblies at runtime from the compiler
    /// directory via an AssemblyLoadContext.Resolving hook, and reuse csc's runtimeconfig.
    /// </summary>
    public class HotReloadSpikeS2WorkerBootstrapTests
    {
        // The Resolving hook must be registered before any Roslyn type is touched, so all Roslyn
        // usage lives in a method that is only JIT-compiled after the hook is in place.
        private const string WorkerSource = @"using System;
using System.IO;
using System.Runtime.Loader;

public static class SpikeWorkerProgram
{
    public static int Main(string[] args)
    {
        if (args.Length != 2)
        {
            Console.Error.WriteLine(""usage: SpikeWorker <roslyn-directory> <source-path>"");
            return 1;
        }

        string roslynDirectoryPath = args[0];
        AssemblyLoadContext.Default.Resolving += (context, assemblyName) =>
        {
            string candidatePath = Path.Combine(roslynDirectoryPath, assemblyName.Name + "".dll"");
            if (File.Exists(candidatePath))
            {
                return context.LoadFromAssemblyPath(candidatePath);
            }

            return null;
        };

        ListMethodNames(args[1]);
        return 0;
    }

    private static void ListMethodNames(string sourcePath)
    {
        string sourceText = File.ReadAllText(sourcePath);
        Microsoft.CodeAnalysis.SyntaxTree syntaxTree = Microsoft.CodeAnalysis.CSharp.CSharpSyntaxTree.ParseText(sourceText);
        foreach (Microsoft.CodeAnalysis.SyntaxNode node in syntaxTree.GetRoot().DescendantNodes())
        {
            if (node is Microsoft.CodeAnalysis.CSharp.Syntax.MethodDeclarationSyntax method)
            {
                Console.WriteLine(method.Identifier.ValueText);
            }
        }
    }
}
";

        private const string ProbeSource = @"public class SpikeProbe
{
    private int _seed;

    public void Alpha()
    {
        _seed = 1;
    }

    public static int Beta(int value)
    {
        return value * 2 + new SpikeProbe()._seed;
    }
}
";

        /// <summary>
        /// What: the bundled csc compiles the worker against the bundled shared framework plus
        /// Roslyn reference assemblies, and the bundled .NET host then runs the worker, which
        /// parses a C# source file with Microsoft.CodeAnalysis and lists its method names.
        /// </summary>
        [Test]
        public async Task BundledRoslynWorker_CompilesAndRunsOnBundledNetCoreHost()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            Assert.That(paths, Is.Not.Null, "External compiler paths could not be resolved for this Unity installation.");
            AssertFileExists(paths.DotnetHostPath, "bundled .NET host");
            AssertFileExists(paths.CompilerDllPath, "bundled csc.dll");
            AssertFileExists(paths.CompilerRuntimeConfigPath, "csc runtimeconfig");
            AssertFileExists(paths.CodeAnalysisDllPath, "Microsoft.CodeAnalysis.dll");
            AssertFileExists(paths.CodeAnalysisCSharpDllPath, "Microsoft.CodeAnalysis.CSharp.dll");
            Assert.That(
                Directory.Exists(paths.NetCoreRuntimeSharedDirectoryPath),
                Is.True,
                $"Shared framework directory not found: {paths.NetCoreRuntimeSharedDirectoryPath}");

            string workRootPath = PrepareCleanDirectory("S2");
            string workerSourcePath = Path.Combine(workRootPath, "SpikeWorker.cs");
            File.WriteAllText(workerSourcePath, WorkerSource);
            string workerDllPath = Path.Combine(workRootPath, "SpikeWorker.dll");
            string responseFilePath = Path.Combine(workRootPath, "SpikeWorker.rsp");
            WriteWorkerResponseFile(responseFilePath, workerSourcePath, workerDllPath, paths);

            (int compileExitCode, string compileStandardOutput, string compileStandardError) = await RunProcessAsync(
                paths.DotnetHostPath,
                $"\"{paths.CompilerDllPath}\" @\"{responseFilePath}\"",
                workRootPath,
                TimeSpan.FromSeconds(120));
            Assert.That(
                compileExitCode,
                Is.EqualTo(0),
                $"Worker compilation failed.\nstdout:\n{compileStandardOutput}\nstderr:\n{compileStandardError}");
            Assert.That(File.Exists(workerDllPath), Is.True, $"Worker dll was not produced: {workerDllPath}");

            // csc's own runtimeconfig pins the bundled shared framework the worker was compiled
            // against; without a runtimeconfig the host refuses to run a framework-dependent dll.
            File.Copy(paths.CompilerRuntimeConfigPath, Path.Combine(workRootPath, "SpikeWorker.runtimeconfig.json"));

            string probeSourcePath = Path.Combine(workRootPath, "SpikeProbe.cs");
            File.WriteAllText(probeSourcePath, ProbeSource);

            string roslynDirectoryPath = Path.GetDirectoryName(paths.CompilerDllPath);
            (int workerExitCode, string workerStandardOutput, string workerStandardError) = await RunProcessAsync(
                paths.DotnetHostPath,
                $"\"{workerDllPath}\" \"{roslynDirectoryPath}\" \"{probeSourcePath}\"",
                workRootPath,
                TimeSpan.FromSeconds(60));
            Assert.That(
                workerExitCode,
                Is.EqualTo(0),
                $"Worker run failed.\nstdout:\n{workerStandardOutput}\nstderr:\n{workerStandardError}");
            Assert.That(workerStandardOutput, Does.Contain("Alpha"), $"Worker output missing method name. stdout:\n{workerStandardOutput}");
            Assert.That(workerStandardOutput, Does.Contain("Beta"), $"Worker output missing method name. stdout:\n{workerStandardOutput}");
        }

        private static void WriteWorkerResponseFile(
            string responseFilePath,
            string workerSourcePath,
            string workerDllPath,
            ExternalCompilerPaths paths)
        {
            // Mirrors RoslynCompilerBackend.WriteCompilerResponseFile, except the worker is an
            // executable compiled against the bundled shared framework instead of Unity Mono.
            List<string> lines = new()
            {
                "-nologo",
                "-nostdlib+",
                "-target:exe",
                $"-out:\"{workerDllPath}\""
            };

            foreach (string referencePath in Directory.GetFiles(paths.NetCoreRuntimeSharedDirectoryPath, "*.dll"))
            {
                lines.Add($"-r:\"{referencePath}\"");
            }

            lines.Add($"-r:\"{paths.CodeAnalysisDllPath}\"");
            lines.Add($"-r:\"{paths.CodeAnalysisCSharpDllPath}\"");
            lines.Add($"\"{workerSourcePath}\"");
            File.WriteAllLines(responseFilePath, lines);
        }

        private static async Task<(int exitCode, string standardOutput, string standardError)> RunProcessAsync(
            string fileName,
            string arguments,
            string workingDirectoryPath,
            TimeSpan timeout)
        {
            ProcessStartInfo startInfo = new()
            {
                FileName = fileName,
                Arguments = arguments,
                WorkingDirectory = workingDirectoryPath,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using Process process = Process.Start(startInfo);
            Assert.That(process, Is.Not.Null, $"Failed to start process: {fileName}");

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            // Task.Run around the blocking WaitForExit mirrors RoslynCompilerBackend's
            // WaitForOneShotCompilerAsync; Process has no awaitable wait on this runtime.
            Task waitForExitTask = Task.Run(() => process.WaitForExit());
            Task completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(timeout));
            if (completedTask != waitForExitTask)
            {
                if (!process.HasExited)
                {
                    try
                    {
                        process.Kill();
                    }
                    catch (InvalidOperationException)
                    {
                        // The process can still exit between the HasExited check and Kill; the
                        // kill exists only for cleanup, so the expected race is swallowed and
                        // the Assert.Fail below stays the single reported diagnosis.
                    }
                }

                // Observe the redirected-stream tasks so a fault during teardown cannot
                // surface later as an unobserved task exception in the Editor.
                _ = Task.WhenAll(stdoutTask, stderrTask).ContinueWith(
                    static task => _ = task.Exception,
                    TaskContinuationOptions.OnlyOnFaulted);
                Assert.Fail($"Process timed out after {timeout.TotalSeconds}s: {fileName} {arguments}");
            }

            // A second WaitForExit flushes the redirected streams after the process exits.
            process.WaitForExit();
            string standardOutput = await stdoutTask;
            string standardError = await stderrTask;
            return (process.ExitCode, standardOutput, standardError);
        }

        private static void AssertFileExists(string path, string label)
        {
            Assert.That(!string.IsNullOrEmpty(path) && File.Exists(path), Is.True, $"{label} not found: {path}");
        }

        private static string PrepareCleanDirectory(string subdirectoryName)
        {
            string projectRootPath = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            string workRootPath = Path.Combine(projectRootPath, "Library", "UloopHotReloadSpike", subdirectoryName);
            if (Directory.Exists(workRootPath))
            {
                Directory.Delete(workRootPath, true);
            }

            Directory.CreateDirectory(workRootPath);
            return workRootPath;
        }
    }
}
