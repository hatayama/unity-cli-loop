using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// How one <see cref="TransformWorkerHost.RunAsync"/> ended. Only <see cref="Completed"/>
    /// carries output. The other kinds tell the caller whether a one-shot fallback is worthwhile:
    /// <see cref="RetryExhausted"/> is the sole kind where the request itself is not known to be
    /// at fault.
    /// </summary>
    internal enum TransformWorkerHostResultKind
    {
        /// <summary>The worker returned exit code 0 and a valid output file.</summary>
        Completed,

        /// <summary>The worker reported a non-zero exit code for this request; the request is at fault.</summary>
        WorkerFailed,

        /// <summary>Two consecutive processes broke the conversation; a fresh one-shot may still succeed.</summary>
        RetryExhausted,

        /// <summary>The worker did not answer within the response timeout and was killed.</summary>
        TimedOut,

        /// <summary>The host was shut down (assembly reload or Editor quit) while the request was in flight.</summary>
        LifecycleClosed,

        /// <summary>The worker could not be compiled or the toolchain could not be resolved.</summary>
        BootstrapFailed
    }

    /// <summary>
    /// Outcome of one resident-worker request.
    /// </summary>
    internal sealed class TransformWorkerHostResult
    {
        public TransformWorkerHostResultKind Kind { get; }
        public TransformWorkerOutputDto Output { get; }
        public string ErrorMessage { get; }

        public bool Success
        {
            get { return Kind == TransformWorkerHostResultKind.Completed; }
        }

        private TransformWorkerHostResult(TransformWorkerHostResultKind kind, TransformWorkerOutputDto output, string errorMessage)
        {
            Kind = kind;
            Output = output;
            ErrorMessage = errorMessage;
        }

        public static TransformWorkerHostResult Completed(TransformWorkerOutputDto output)
        {
            Debug.Assert(output != null, "output must not be null.");
            return new TransformWorkerHostResult(TransformWorkerHostResultKind.Completed, output, string.Empty);
        }

        public static TransformWorkerHostResult Failure(TransformWorkerHostResultKind kind, string errorMessage)
        {
            Debug.Assert(kind != TransformWorkerHostResultKind.Completed, "Completed must carry output.");
            Debug.Assert(!string.IsNullOrEmpty(errorMessage), "errorMessage must not be empty.");
            return new TransformWorkerHostResult(kind, null, errorMessage);
        }
    }

    /// <summary>
    /// Where the next worker process starts from: the compiled worker directory and the dotnet
    /// host to run it with. Resolved once per request, before any process is touched, so a
    /// worker-source change is observed as a directory change and restarts the resident process.
    /// </summary>
    internal sealed class TransformWorkerLaunchTarget
    {
        public bool Success { get; }
        public string WorkerDirectory { get; }
        public string DotnetHostPath { get; }
        public string ErrorMessage { get; }

        private TransformWorkerLaunchTarget(bool success, string workerDirectory, string dotnetHostPath, string errorMessage)
        {
            Success = success;
            WorkerDirectory = workerDirectory;
            DotnetHostPath = dotnetHostPath;
            ErrorMessage = errorMessage;
        }

        public static TransformWorkerLaunchTarget Resolved(string workerDirectory, string dotnetHostPath)
        {
            Debug.Assert(!string.IsNullOrEmpty(workerDirectory), "workerDirectory must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(dotnetHostPath), "dotnetHostPath must not be empty.");
            return new TransformWorkerLaunchTarget(true, workerDirectory, dotnetHostPath, string.Empty);
        }

        public static TransformWorkerLaunchTarget Failure(string errorMessage)
        {
            Debug.Assert(!string.IsNullOrEmpty(errorMessage), "errorMessage must not be empty.");
            return new TransformWorkerLaunchTarget(false, null, null, errorMessage);
        }
    }

    /// <summary>
    /// Resolves the launch target for the next request. Injected so host tests can run without
    /// compiling the real worker.
    /// </summary>
    internal delegate Task<TransformWorkerLaunchTarget> TransformWorkerLaunchTargetResolver(CancellationToken ct);

    /// <summary>
    /// The production resolver: compiles the worker on demand and reads the dotnet host path
    /// from the Unity installation.
    /// </summary>
    internal static class TransformWorkerLaunchTargetResolution
    {
        public static async Task<TransformWorkerLaunchTarget> ResolveAsync(CancellationToken ct)
        {
            TransformWorkerBootstrapResult bootstrapResult =
                await TransformWorkerBootstrap.EnsureWorkerAsync(ct).ConfigureAwait(false);
            if (!bootstrapResult.Success)
            {
                return TransformWorkerLaunchTarget.Failure(bootstrapResult.ErrorMessage);
            }

            // ExternalCompilerPathResolver reads EditorApplication.applicationPath.
            await MainThreadSwitcher.SwitchToMainThread(ct);
            ExternalCompilerPaths paths = ExternalCompilerPathResolver.Resolve();
            if (paths == null)
            {
                return TransformWorkerLaunchTarget.Failure(
                    "External compiler paths could not be resolved for this Unity installation.");
            }

            return TransformWorkerLaunchTarget.Resolved(bootstrapResult.WorkerDirectory, paths.DotnetHostPath);
        }
    }
}
