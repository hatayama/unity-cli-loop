using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using UnityEditor.Compilation;
using Debug = UnityEngine.Debug;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the shared Roslyn compiler worker and configures its .NET process environment.
    /// </summary>
    internal static class SharedRoslynCompilerWorkerAssemblyBuilder
    {
        private const int WorkerAssemblyBuildTimeoutMilliseconds = 30000;
        internal const string DotnetMultilevelLookupEnvironmentVariableName = "DOTNET_MULTILEVEL_LOOKUP";
        internal const string DotnetMultilevelLookupDisabledValue = "0";

        /// <summary>
        /// Carries the result data produced by Worker Assembly Build behavior.
        /// </summary>
        internal sealed class WorkerAssemblyBuildResult
        {
            public bool StartedSuccessfully { get; }

            public CompilerMessage[] Messages { get; }

            public string FailureReason { get; }

            public object FailureContext { get; }

            private WorkerAssemblyBuildResult(
                bool startedSuccessfully,
                CompilerMessage[] messages,
                string failureReason,
                object failureContext)
            {
                StartedSuccessfully = startedSuccessfully;
                Messages = messages;
                FailureReason = failureReason;
                FailureContext = failureContext;
            }

            public static WorkerAssemblyBuildResult Started(CompilerMessage[] messages)
            {
                return new WorkerAssemblyBuildResult(true, messages, null, null);
            }

            public static WorkerAssemblyBuildResult StartFailure(string failureReason, object failureContext)
            {
                return new WorkerAssemblyBuildResult(false, null, failureReason, failureContext);
            }
        }

        internal static void ConfigureWorkerDotnetRuntimeEnvironment(ProcessStartInfo startInfo)
        {
            Debug.Assert(startInfo != null, "startInfo must not be null");

            // Why: global probing can select a system .NET 6 runtime while Unity 6000.4 worker
            // references come from the bundled .NET 8 runtime, which breaks assembly binding.
            startInfo.EnvironmentVariables[DotnetMultilevelLookupEnvironmentVariableName] =
                DotnetMultilevelLookupDisabledValue;
        }

        internal static WorkerAssemblyBuildResult CompileWorkerAssembly(
            ExternalCompilerPaths externalCompilerPaths,
            string workerSourcePath,
            string workerAssemblyPath,
            string workerCompileResponseFilePath)
        {
            WriteWorkerCompilerResponseFile(
                workerCompileResponseFilePath,
                workerSourcePath,
                workerAssemblyPath,
                BuildWorkerReferenceSet(externalCompilerPaths));

            ProcessStartInfo startInfo = new()            {
                FileName = externalCompilerPaths.DotnetHostPath,
                Arguments = $"{QuoteCommandLineArgument(externalCompilerPaths.CompilerDllPath)} @{QuoteCommandLineArgument(workerCompileResponseFilePath)}",
                WorkingDirectory = Path.GetDirectoryName(workerSourcePath),
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };
            ConfigureWorkerDotnetRuntimeEnvironment(startInfo);

            using Process process = ProcessStartHelper.TryStart(startInfo);
            if (process == null)
            {
                return WorkerAssemblyBuildResult.StartFailure(
                    "worker_compiler_start_failed",
                    new
                    {
                        dotnet_host_path = externalCompilerPaths.DotnetHostPath,
                        compiler_dll_path = externalCompilerPaths.CompilerDllPath
                    });
            }

            Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
            Task<string> stderrTask = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(WorkerAssemblyBuildTimeoutMilliseconds))
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(500);
                }

                Task.WaitAll(stdoutTask, stderrTask);
                return WorkerAssemblyBuildResult.StartFailure(
                    "worker_compiler_timeout",
                    new
                    {
                        timeout_ms = WorkerAssemblyBuildTimeoutMilliseconds,
                        dotnet_host_path = externalCompilerPaths.DotnetHostPath,
                        compiler_dll_path = externalCompilerPaths.CompilerDllPath
                    });
            }

            Task.WaitAll(stdoutTask, stderrTask);
            CompilerMessage[] compilerMessages = ExternalCompilerMessageParser.Parse(
                stdoutTask.GetAwaiter().GetResult(),
                stderrTask.GetAwaiter().GetResult(),
                process.ExitCode);
            return WorkerAssemblyBuildResult.Started(compilerMessages);
        }

        private static List<string> BuildWorkerReferenceSet(ExternalCompilerPaths externalCompilerPaths)
        {
            string sharedRuntimeDirectoryPath = externalCompilerPaths.NetCoreRuntimeSharedDirectoryPath;
            List<string> references = new()            {
                Path.Combine(sharedRuntimeDirectoryPath, "System.Private.CoreLib.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Runtime.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Console.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Collections.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.IO.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Threading.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Threading.Tasks.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Text.Encoding.Extensions.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "System.Runtime.Extensions.dll"),
                Path.Combine(sharedRuntimeDirectoryPath, "netstandard.dll"),
                externalCompilerPaths.CodeAnalysisDllPath,
                externalCompilerPaths.CodeAnalysisCSharpDllPath
            };

            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Collections.Immutable.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Reflection.Metadata.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Runtime.CompilerServices.Unsafe.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Memory.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Buffers.dll"));
            AddIfExists(references, Path.Combine(sharedRuntimeDirectoryPath, "System.Threading.Tasks.Extensions.dll"));

            return references;
        }

        private static void WriteWorkerCompilerResponseFile(
            string responseFilePath,
            string sourcePath,
            string dllPath,
            IReadOnlyCollection<string> references)
        {
            List<string> lines = new()            {
                "-nologo",
                "-nostdlib+",
                "-target:exe",
                "-optimize+",
                "-debug-",
                QuoteResponseFileArgument("-out:", dllPath)
            };

            foreach (string reference in references)
            {
                lines.Add(QuoteResponseFileArgument("-r:", reference));
            }

            lines.Add(QuoteResponseFilePath(sourcePath));
            File.WriteAllLines(responseFilePath, lines);
        }

        internal static void DeleteWorkerAssemblyIfPresent(string assemblyPath)
        {
            if (File.Exists(assemblyPath))
            {
                File.Delete(assemblyPath);
            }
        }

        private static string QuoteResponseFileArgument(string prefix, string value)
        {
            return $"{prefix}{QuoteResponseFilePath(value)}";
        }

        private static string QuoteResponseFilePath(string path)
        {
            return $"\"{path}\"";
        }

        internal static string QuoteCommandLineArgument(string value)
        {
            return $"\"{value}\"";
        }

        private static void AddIfExists(
            List<string> destination,
            string referencePath)
        {
            if (string.IsNullOrEmpty(referencePath) || !File.Exists(referencePath))
            {
                return;
            }

            destination.Add(referencePath);
        }
    }
}
