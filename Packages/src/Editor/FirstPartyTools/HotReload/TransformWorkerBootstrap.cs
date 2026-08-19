using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using UnityEditor.PackageManager;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

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
        /// <c>Library/UloopHotReload/Worker/&lt;hash&gt;/</c> and returns that directory.
        /// </summary>
        public static async Task<TransformWorkerBootstrapResult> EnsureWorkerAsync(CancellationToken ct)
        {
            // Resolver / PackageInfo / Application.dataPath all require the Unity main thread.
            await MainThreadSwitcher.SwitchToMainThread(ct);

            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            if (paths == null)
            {
                return TransformWorkerBootstrapResult.Failure(
                    "External compiler paths could not be resolved for this Unity installation.");
            }

            string workerSourceDirectory = ResolveWorkerSourceDirectory();
            string[] workerSourcePaths = EnumerateWorkerSourceFiles(workerSourceDirectory);
            if (workerSourcePaths.Length == 0)
            {
                return TransformWorkerBootstrapResult.Failure(
                    "Transform worker source not found: " + workerSourceDirectory);
            }

            // Include toolchain paths so a Unity Editor upgrade cannot reuse a stale sidecar that
            // points at a removed Roslyn directory under Library/.
            string cacheKey = ComputeCacheKey(workerSourcePaths, paths);
            string cacheDirectory = Path.Combine(ResolveWorkerCacheRoot(), cacheKey);
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
                workerSourcePaths,
                workerDllPath,
                cacheDirectory,
                ct).ConfigureAwait(false);
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

        internal static string ResolveWorkerSourceDirectory()
        {
            PackageInfo packageInfo = PackageInfo.FindForAssembly(typeof(TransformWorkerBootstrap).Assembly);
            Debug.Assert(packageInfo != null, "PackageInfo must resolve for the HotReload assembly.");
            return Path.Combine(packageInfo.resolvedPath, HotReloadConstants.WorkerSourcePackageRelativePath);
        }

        // why: Directory.GetFiles order is unspecified, so ordinal file-name sort keeps the
        // response file and cache key identical on Windows and macOS.
        internal static string[] EnumerateWorkerSourceFiles(string workerSourceDirectory)
        {
            if (!Directory.Exists(workerSourceDirectory))
            {
                return Array.Empty<string>();
            }

            string[] sourcePaths = Directory.GetFiles(workerSourceDirectory, "*.cs");
            Array.Sort(sourcePaths, CompareWorkerSourceFileNamesOrdinal);
            return sourcePaths;
        }

        private static int CompareWorkerSourceFileNamesOrdinal(string left, string right)
        {
            return string.Compare(
                Path.GetFileName(left),
                Path.GetFileName(right),
                StringComparison.Ordinal);
        }

        private static async Task<TransformWorkerBootstrapResult> CompileWorkerAsync(
            ExternalCompilerPaths paths,
            IReadOnlyList<string> workerSourcePaths,
            string workerDllPath,
            string cacheDirectory,
            CancellationToken ct)
        {
            string responseFilePath = Path.Combine(cacheDirectory, HotReloadConstants.WorkerResponseFileName);
            WriteWorkerResponseFile(responseFilePath, workerSourcePaths, workerDllPath, paths);

            (int exitCode, string standardOutput, string standardError) = await HotReloadProcessRunner.RunAsync(
                paths.DotnetHostPath,
                "\"" + paths.CompilerDllPath + "\" @\"" + responseFilePath + "\"",
                cacheDirectory,
                TimeSpan.FromMilliseconds(HotReloadConstants.WorkerProcessTimeoutMilliseconds),
                ct).ConfigureAwait(false);

            if (exitCode != 0)
            {
                return TransformWorkerBootstrapResult.Failure(
                    "Transform worker compilation failed.\nstdout:\n" + standardOutput
                    + "\nstderr:\n" + standardError);
            }

            return TransformWorkerBootstrapResult.SuccessResult(cacheDirectory);
        }

        internal static void WriteWorkerResponseFile(
            string responseFilePath,
            IReadOnlyList<string> workerSourcePaths,
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
                // Why: on Windows the bundled shared framework also ships native PE images
                // (ucrtbase.dll, coreclr.dll, api-ms-win-crt-*.dll, ...); passing those to csc
                // as references fails the worker build with CS0009.
                if (!ManagedAssemblyDetector.IsManagedAssembly(referencePath))
                {
                    continue;
                }

                lines.Add("-r:\"" + referencePath + "\"");
            }

            lines.Add("-r:\"" + paths.CodeAnalysisDllPath + "\"");
            lines.Add("-r:\"" + paths.CodeAnalysisCSharpDllPath + "\"");
            for (int index = 0; index < workerSourcePaths.Count; index++)
            {
                lines.Add("\"" + workerSourcePaths[index] + "\"");
            }

            File.WriteAllLines(responseFilePath, lines);
        }

        internal static string ComputeCacheKey(
            IReadOnlyList<string> workerSourcePaths,
            ExternalCompilerPaths paths)
        {
            using SHA256 sha256 = SHA256.Create();
            byte[] sourceHash = HashWorkerSourceFiles(workerSourcePaths);

            string toolchainIdentity = string.Join(
                "|",
                paths.CompilerDllPath ?? string.Empty,
                paths.CompilerRuntimeConfigPath ?? string.Empty,
                paths.DotnetHostPath ?? string.Empty,
                paths.CodeAnalysisDllPath ?? string.Empty,
                paths.CodeAnalysisCSharpDllPath ?? string.Empty);
            byte[] toolchainBytes = Encoding.UTF8.GetBytes(toolchainIdentity);
            byte[] toolchainHash = sha256.ComputeHash(toolchainBytes);

            byte[] combined = new byte[sourceHash.Length + toolchainHash.Length];
            Buffer.BlockCopy(sourceHash, 0, combined, 0, sourceHash.Length);
            Buffer.BlockCopy(toolchainHash, 0, combined, sourceHash.Length, toolchainHash.Length);
            byte[] finalHash = sha256.ComputeHash(combined);

            StringBuilder builder = new StringBuilder(finalHash.Length * 2);
            foreach (byte value in finalHash)
            {
                builder.Append(value.ToString("x2"));
            }

            return builder.ToString();
        }

        // why: the cache must miss when either a source file name or its bytes change, not only
        // when a single canonical file's content changes.
        internal static byte[] HashWorkerSourceFiles(IReadOnlyList<string> workerSourcePaths)
        {
            using SHA256 sha256 = SHA256.Create();
            using MemoryStream buffer = new MemoryStream();
            for (int index = 0; index < workerSourcePaths.Count; index++)
            {
                string sourcePath = workerSourcePaths[index];
                byte[] nameBytes = Encoding.UTF8.GetBytes(Path.GetFileName(sourcePath));
                buffer.Write(nameBytes, 0, nameBytes.Length);
                buffer.WriteByte(0);
                byte[] contentBytes = File.ReadAllBytes(sourcePath);
                buffer.Write(contentBytes, 0, contentBytes.Length);
                buffer.WriteByte(0);
            }

            return sha256.ComputeHash(buffer.ToArray());
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
