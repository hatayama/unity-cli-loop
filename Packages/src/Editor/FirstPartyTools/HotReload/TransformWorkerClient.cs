using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs transform requests through the resident worker host, falling back to a single one-shot
    /// worker process when the resident conversation cannot be held.
    /// </summary>
    internal static class TransformWorkerClient
    {
        // Why a test seam on a static class: the client has no instance to inject into, and the
        // resident host must be replaceable so routing tests do not depend on the real worker.
        internal static TransformWorkerHost HostOverrideForTests;

        /// <summary>
        /// Transforms <paramref name="input"/> through the resident worker, and only when two fresh
        /// resident processes broke the conversation, once more through a one-shot worker process.
        /// </summary>
        public static async Task<TransformWorkerClientResult> RunAsync(
            TransformWorkerInputDto input,
            CancellationToken ct)
        {
            Debug.Assert(input != null, "input must not be null.");
            Debug.Assert(input.sources != null, "sources must not be null.");
            Debug.Assert(input.sources.Length > 0, "sources must not be empty.");

            TransformWorkerHost host = HostOverrideForTests ?? TransformWorkerHost.Shared;
            TransformWorkerHostResult hostResult = await host.RunAsync(input, ct).ConfigureAwait(false);
            if (hostResult.Kind == TransformWorkerHostResultKind.Completed)
            {
                Debug.Assert(
                    hostResult.Output.files.Length == input.sources.Length,
                    "A successful worker run must return one per-file output per source.");
                return TransformWorkerClientResult.SuccessResult(hostResult.Output);
            }

            // WorkerFailed, TimedOut, BootstrapFailed and LifecycleClosed describe the request or a
            // deliberate stop, so repeating them on a one-shot process only costs time.
            if (hostResult.Kind != TransformWorkerHostResultKind.RetryExhausted)
            {
                return TransformWorkerClientResult.Failure(hostResult.ErrorMessage);
            }

            // Why fall back only here: two fresh processes broke the conversation without the worker
            // reporting anything, so the resident path itself is suspect and a one-shot process is
            // the only way to still serve this run.
            VibeLogger.LogWarning(
                HotReloadConstants.VibeLogWorkerHostFallbackOneShot,
                "Resident transform worker conversation broke twice; running this request as a one-shot process.",
                new { reason = hostResult.ErrorMessage });
            return await RunOneShotAsync(input, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Bootstraps the worker if needed, writes <paramref name="input"/> to a temp JSON file, runs
        /// <c>dotnet worker.dll &lt;in&gt; &lt;out&gt;</c> once, and deserializes the output.
        /// </summary>
        private static async Task<TransformWorkerClientResult> RunOneShotAsync(
            TransformWorkerInputDto input,
            CancellationToken ct)
        {
            TransformWorkerBootstrapResult bootstrapResult =
                await TransformWorkerBootstrap.EnsureWorkerAsync(ct).ConfigureAwait(false);
            if (!bootstrapResult.Success)
            {
                return TransformWorkerClientResult.Failure(bootstrapResult.ErrorMessage);
            }

            // ExternalCompilerPathResolver reads EditorApplication.applicationPath.
            await MainThreadSwitcher.SwitchToMainThread(ct);
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
                (int exitCode, string standardOutput, string standardError) = await HotReloadProcessRunner.RunAsync(
                    paths.DotnetHostPath,
                    arguments,
                    bootstrapResult.WorkerDirectory,
                    TimeSpan.FromMilliseconds(HotReloadConstants.WorkerProcessTimeoutMilliseconds),
                    ct).ConfigureAwait(false);

                if (exitCode != 0)
                {
                    return TransformWorkerClientResult.Failure(
                        "Transform worker exited with code " + exitCode
                        + ".\nstdout:\n" + standardOutput
                        + "\nstderr:\n" + standardError);
                }

                TransformWorkerOutputDto output = TransformWorkerOutputReader.TryRead(
                    outputJsonPath,
                    input.sources.Length,
                    out string readError);
                if (output == null)
                {
                    return TransformWorkerClientResult.Failure(readError);
                }

                // Why fail here: run-level parseErrors describe a failure that belongs to no
                // single source, so there is no per-file row to carry it. Turning it into a
                // client failure at the process boundary is what makes the per-file row count
                // an invariant for every caller downstream.
                if (output.parseErrors.Length > 0)
                {
                    return TransformWorkerClientResult.Failure(string.Join("\n", output.parseErrors));
                }

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

        // Why internal: tests must exercise this path instead of re-implementing ??=.
        internal static void CoalesceOutput(TransformWorkerOutputDto output)
        {
            Debug.Assert(output != null, "output must not be null.");

            output.entries ??= Array.Empty<TransformWorkerEntryDto>();
            output.skipped ??= Array.Empty<TransformWorkerSkippedDto>();
            output.files ??= Array.Empty<TransformWorkerFileOutputDto>();
            output.parseErrors ??= Array.Empty<string>();
            output.siblingConstDriftWarnings ??= Array.Empty<string>();
            output.unchangedMethods ??= Array.Empty<TransformWorkerUnchangedMethodDto>();
            output.shimSource ??= string.Empty;
            foreach (TransformWorkerFileOutputDto fileOutput in output.files)
            {
                if (fileOutput == null)
                {
                    continue;
                }

                fileOutput.sourceContentSha256 ??= string.Empty;
                fileOutput.parseErrors ??= Array.Empty<string>();
                fileOutput.declarationDriftWarnings ??= Array.Empty<string>();
                fileOutput.removedMembers ??= Array.Empty<TransformWorkerRemovedMemberDto>();
                fileOutput.removedMethodSignatures ??= Array.Empty<TransformWorkerRemovedMethodSignatureDto>();
                fileOutput.addedFieldNames ??= Array.Empty<string>();
                fileOutput.addedConstNames ??= Array.Empty<string>();
            }

            foreach (TransformWorkerEntryDto entry in output.entries)
            {
                if (entry == null)
                {
                    continue;
                }

                entry.patchKind ??= string.Empty;
                entry.calledAddedMethodKeys ??= Array.Empty<string>();
                entry.parameterTypeFullNames ??= Array.Empty<string>();
            }
        }

        private static void WriteUtf8NoBom(string path, string contents)
        {
            File.WriteAllText(path, contents, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
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
