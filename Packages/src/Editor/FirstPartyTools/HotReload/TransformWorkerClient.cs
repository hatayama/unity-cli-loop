using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Runs transform requests through the resident worker host, falling back to a single one-shot
    /// worker process when the resident conversation cannot be held.
    /// </summary>
    internal sealed class TransformWorkerClient
    {
        private readonly TransformWorkerHost _host;

        internal TransformWorkerClient(TransformWorkerHost host)
        {
            Debug.Assert(host != null, "host must not be null.");
            _host = host;
        }

        /// <summary>
        /// The host this client routes through, so a replacement that substitutes another
        /// collaborator can keep the routing the installed services already have.
        /// </summary>
        internal TransformWorkerHost Host => _host;

        /// <summary>
        /// Transforms <paramref name="input"/> through the resident worker, and only when two fresh
        /// resident processes broke the conversation, once more through a one-shot worker process.
        /// </summary>
        public async Task<TransformWorkerClientResult> RunAsync(
            TransformWorkerInputDto input,
            CancellationToken ct)
        {
            Debug.Assert(input != null, "input must not be null.");
            Debug.Assert(input.sources != null, "sources must not be null.");
            Debug.Assert(input.sources.Length > 0, "sources must not be empty.");

            // Why before the worker runs: a record the worker cannot act on does not always fail
            // there. A record missing its owner or its fingerprint still builds a valid mapping,
            // and the run then silently binds the retained type back to its source.
            if (!new TransformWorkerOutputValidator().TryValidateIntroducedTypeArtifacts(input, out string artifactError))
            {
                return TransformWorkerClientResult.Failure(artifactError);
            }

            return await RunWorkerAsync(input, ct).ConfigureAwait(false);
        }

        /// <summary>
        /// Runs the worker on a request that has already been checked, without checking it again:
        /// through the resident host, and only when two fresh resident processes broke the
        /// conversation, once more through a one-shot worker process.
        /// </summary>
        // Why separate from RunAsync: the worker consumes the request JSON as a boundary of its
        // own and has to refuse a record it cannot act on even when the request did not come from
        // this client. Its guard can only be shown by handing it a request this client would have
        // refused first.
        internal async Task<TransformWorkerClientResult> RunWorkerAsync(
            TransformWorkerInputDto input,
            CancellationToken ct)
        {
            TransformWorkerHostResult hostResult = await _host.RunAsync(input, ct).ConfigureAwait(false);
            if (hostResult.Kind == TransformWorkerHostResultKind.Completed)
            {
                // Why the resident output is interpreted here and not inside the host: the host
                // hands back the document as the worker wrote it, and the ordered boundary checks
                // are what turn it into a result. Keeping them on this side is also what stops a
                // replaced host from returning an output that never met them.
                return CreateOutputInterpreter().InterpretOutput(input, hostResult.Output);
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
        private async Task<TransformWorkerClientResult> RunOneShotAsync(
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

            using (TransformWorkerRequestFiles requestFiles = new TransformWorkerRequestFiles())
            {
                string inputJsonPath = requestFiles.WriteInputJson(input);
                string outputJsonPath = requestFiles.OutputJsonPath;

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
                    out string readError);
                if (output == null)
                {
                    return TransformWorkerClientResult.Failure(readError);
                }

                return CreateOutputInterpreter().InterpretOutput(input, output);
            }
        }

        private static TransformWorkerOutputInterpreter CreateOutputInterpreter()
        {
            return new TransformWorkerOutputInterpreter(new TransformWorkerOutputValidator());
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
