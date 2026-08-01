using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

using UnityEditor.PackageManager;

using UnityEngine;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compiles and caches the out-of-process transform worker against the Unity-bundled Roslyn
    /// and .NET host (same bootstrap shape as spike S2).
    /// </summary>
    internal static class TransformWorkerBootstrap
    {
        /// <summary>
        /// Ensures a worker.dll matching the current worker source exists under
        /// <c>Library/UloopHotReload/Worker/&lt;sha256&gt;/</c> and returns that directory.
        /// </summary>
        public static async Task<TransformWorkerBootstrapResult> EnsureWorkerAsync()
        {
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            if (paths == null)
            {
                return TransformWorkerBootstrapResult.Failure(
                    "External compiler paths could not be resolved for this Unity installation.");
            }

            string workerSourcePath = ResolveWorkerSourcePath();
            if (!File.Exists(workerSourcePath))
            {
                return TransformWorkerBootstrapResult.Failure(
                    "Transform worker source not found: " + workerSourcePath);
            }

            string sourceHash = ComputeSha256Hex(workerSourcePath);
            string cacheDirectory = Path.Combine(ResolveWorkerCacheRoot(), sourceHash);
            string workerDllPath = Path.Combine(cacheDirectory, HotReloadConstants.WorkerDllFileName);
            string runtimeConfigPath = Path.Combine(cacheDirectory, HotReloadConstants.WorkerRuntimeConfigFileName);
            string roslynSidecarPath = Path.Combine(
                cacheDirectory,
                HotReloadConstants.WorkerRoslynDirectorySidecarFileName);

            if (File.Exists(workerDllPath) && File.Exists(runtimeConfigPath) && File.Exists(roslynSidecarPath))
            {
                return TransformWorkerBootstrapResult.SuccessResult(cacheDirectory);
            }

            Directory.CreateDirectory(cacheDirectory);
            TransformWorkerBootstrapResult compileResult = await CompileWorkerAsync(
                paths,
                workerSourcePath,
                workerDllPath,
                cacheDirectory).ConfigureAwait(true);
            if (!compileResult.Success)
            {
                return compileResult;
            }

            File.Copy(paths.CompilerRuntimeConfigPath, runtimeConfigPath, overwrite: true);

            // Sidecar lets the worker register an ALC Resolving hook without changing the
            // <in> <out> argv contract from the plan.
            string roslynDirectoryPath = Path.GetDirectoryName(paths.CompilerDllPath);
            File.WriteAllText(
                roslynSidecarPath,
                roslynDirectoryPath + Environment.NewLine,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            if (!File.Exists(workerDllPath))
            {
                return TransformWorkerBootstrapResult.Failure(
                    "Worker dll was not produced: " + workerDllPath);
            }

            return TransformWorkerBootstrapResult.SuccessResult(cacheDirectory);
        }

        internal static string ResolveWorkerSourcePath()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(TransformWorkerBootstrap).Assembly);
            Debug.Assert(packageInfo != null, "PackageInfo must resolve for the HotReload assembly.");
            return Path.Combine(packageInfo.resolvedPath, HotReloadConstants.WorkerSourcePackageRelativePath);
        }

        private static async Task<TransformWorkerBootstrapResult> CompileWorkerAsync(
            ExternalCompilerPaths paths,
            string workerSourcePath,
            string workerDllPath,
            string cacheDirectory)
        {
            string responseFilePath = Path.Combine(cacheDirectory, HotReloadConstants.WorkerResponseFileName);
            WriteWorkerResponseFile(responseFilePath, workerSourcePath, workerDllPath, paths);

            (int exitCode, string standardOutput, string standardError) = await RunProcessAsync(
                paths.DotnetHostPath,
                "\"" + paths.CompilerDllPath + "\" @\"" + responseFilePath + "\"",
                cacheDirectory,
                TimeSpan.FromMilliseconds(HotReloadConstants.WorkerProcessTimeoutMilliseconds)).ConfigureAwait(true);

            if (exitCode != 0)
            {
                return TransformWorkerBootstrapResult.Failure(
                    "Transform worker compilation failed.\nstdout:\n" + standardOutput
                    + "\nstderr:\n" + standardError);
            }

            return TransformWorkerBootstrapResult.SuccessResult(cacheDirectory);
        }

        private static void WriteWorkerResponseFile(
            string responseFilePath,
            string workerSourcePath,
            string workerDllPath,
            ExternalCompilerPaths paths)
        {
            // Mirrors spike S2: framework-dependent exe against the bundled shared framework + Roslyn.
            List<string> lines = new List<string>
            {
                "-nologo",
                "-nostdlib+",
                "-target:exe",
                "-out:\"" + workerDllPath + "\""
            };

            foreach (string referencePath in Directory.GetFiles(paths.NetCoreRuntimeSharedDirectoryPath, "*.dll"))
            {
                lines.Add("-r:\"" + referencePath + "\"");
            }

            lines.Add("-r:\"" + paths.CodeAnalysisDllPath + "\"");
            lines.Add("-r:\"" + paths.CodeAnalysisCSharpDllPath + "\"");
            lines.Add("\"" + workerSourcePath + "\"");
            File.WriteAllLines(responseFilePath, lines);
        }

        private static async Task<(int exitCode, string standardOutput, string standardError)> RunProcessAsync(
            string fileName,
            string arguments,
            string workingDirectoryPath,
            TimeSpan timeout)
        {
            ProcessStartInfo startInfo = new ProcessStartInfo
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
            Debug.Assert(process != null, "Failed to start process: " + fileName);

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            // Task.Run around WaitForExit mirrors RoslynCompilerBackend / spike S2 — Process has
            // no awaitable wait on this runtime.
            Task waitForExitTask = Task.Run(() => process.WaitForExit());
            Task completedTask = await Task.WhenAny(waitForExitTask, Task.Delay(timeout)).ConfigureAwait(true);
            if (completedTask != waitForExitTask)
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }

                return (-1, string.Empty, "Process timed out after " + timeout.TotalSeconds + "s.");
            }

            process.WaitForExit();
            string standardOutput = await stdoutTask.ConfigureAwait(true);
            string standardError = await stderrTask.ConfigureAwait(true);
            return (process.ExitCode, standardOutput, standardError);
        }

        private static string ComputeSha256Hex(string filePath)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(filePath);
            byte[] hash = sha256.ComputeHash(stream);
            StringBuilder builder = new StringBuilder(hash.Length * 2);
            foreach (byte value in hash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        private static string ResolveWorkerCacheRoot()
        {
            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
            return Path.Combine(projectRoot, HotReloadConstants.WorkerCacheRelativeDirectory);
        }
    }

    /// <summary>
    /// Outcome of ensuring a cached transform worker binary exists.
    /// </summary>
    internal sealed class TransformWorkerBootstrapResult
    {
        public bool Success { get; }
        public string WorkerDirectory { get; }
        public string ErrorMessage { get; }

        private TransformWorkerBootstrapResult(bool success, string workerDirectory, string errorMessage)
        {
            Success = success;
            WorkerDirectory = workerDirectory;
            ErrorMessage = errorMessage;
        }

        public static TransformWorkerBootstrapResult SuccessResult(string workerDirectory)
        {
            return new TransformWorkerBootstrapResult(true, workerDirectory, string.Empty);
        }

        public static TransformWorkerBootstrapResult Failure(string errorMessage)
        {
            return new TransformWorkerBootstrapResult(false, null, errorMessage);
        }
    }
}
