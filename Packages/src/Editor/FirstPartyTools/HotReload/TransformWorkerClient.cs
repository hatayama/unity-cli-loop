using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using Newtonsoft.Json;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs the cached transform worker with file-path JSON I/O (UTF-8, no BOM).
    /// </summary>
    internal static class TransformWorkerClient
    {
        /// <summary>
        /// Bootstraps the worker if needed, writes <paramref name="input"/> to a temp JSON file,
        /// runs <c>dotnet worker.dll &lt;in&gt; &lt;out&gt;</c>, and deserializes the output.
        /// </summary>
        public static async Task<TransformWorkerClientResult> RunAsync(TransformWorkerInputDto input)
        {
            Debug.Assert(input != null, "input must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(input.sourcePath), "sourcePath must not be empty.");

            TransformWorkerBootstrapResult bootstrapResult =
                await TransformWorkerBootstrap.EnsureWorkerAsync().ConfigureAwait(true);
            if (!bootstrapResult.Success)
            {
                return TransformWorkerClientResult.Failure(bootstrapResult.ErrorMessage);
            }

            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            if (paths == null)
            {
                return TransformWorkerClientResult.Failure(
                    "External compiler paths could not be resolved for this Unity installation.");
            }

            string tempDirectory = Path.Combine(Path.GetTempPath(), "uloop-hot-reload-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(tempDirectory);
            string inputJsonPath = Path.Combine(tempDirectory, "input.json");
            string outputJsonPath = Path.Combine(tempDirectory, "output.json");

            try
            {
                WriteUtf8NoBom(inputJsonPath, JsonConvert.SerializeObject(input));

                string workerDllPath = Path.Combine(
                    bootstrapResult.WorkerDirectory,
                    HotReloadConstants.WorkerDllFileName);
                string arguments = "\"" + workerDllPath + "\" \"" + inputJsonPath + "\" \"" + outputJsonPath + "\"";
                (int exitCode, string standardOutput, string standardError) = await RunProcessAsync(
                    paths.DotnetHostPath,
                    arguments,
                    bootstrapResult.WorkerDirectory,
                    TimeSpan.FromMilliseconds(HotReloadConstants.WorkerProcessTimeoutMilliseconds)).ConfigureAwait(true);

                if (exitCode != 0)
                {
                    return TransformWorkerClientResult.Failure(
                        "Transform worker exited with code " + exitCode
                        + ".\nstdout:\n" + standardOutput
                        + "\nstderr:\n" + standardError);
                }

                if (!File.Exists(outputJsonPath))
                {
                    return TransformWorkerClientResult.Failure(
                        "Transform worker did not produce an output JSON file.");
                }

                string outputJson = File.ReadAllText(
                    outputJsonPath,
                    new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                TransformWorkerOutputDto output = JsonConvert.DeserializeObject<TransformWorkerOutputDto>(outputJson);
                if (output == null)
                {
                    return TransformWorkerClientResult.Failure(
                        "Failed to deserialize transform worker output JSON.");
                }

                output.entries ??= Array.Empty<TransformWorkerEntryDto>();
                output.skipped ??= Array.Empty<TransformWorkerSkippedDto>();
                output.parseErrors ??= Array.Empty<string>();
                output.shimSource ??= string.Empty;
                return TransformWorkerClientResult.SuccessResult(output);
            }
            finally
            {
                if (Directory.Exists(tempDirectory))
                {
                    Directory.Delete(tempDirectory, recursive: true);
                }
            }
        }

        private static void WriteUtf8NoBom(string path, string contents)
        {
            File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
    }

    /// <summary>
    /// Outcome of one transform-worker invocation.
    /// </summary>
    internal sealed class TransformWorkerClientResult
    {
        public bool Success { get; }
        public TransformWorkerOutputDto Output { get; }
        public string ErrorMessage { get; }

        private TransformWorkerClientResult(bool success, TransformWorkerOutputDto output, string errorMessage)
        {
            Success = success;
            Output = output;
            ErrorMessage = errorMessage;
        }

        public static TransformWorkerClientResult SuccessResult(TransformWorkerOutputDto output)
        {
            return new TransformWorkerClientResult(true, output, string.Empty);
        }

        public static TransformWorkerClientResult Failure(string errorMessage)
        {
            return new TransformWorkerClientResult(false, null, errorMessage);
        }
    }
}
