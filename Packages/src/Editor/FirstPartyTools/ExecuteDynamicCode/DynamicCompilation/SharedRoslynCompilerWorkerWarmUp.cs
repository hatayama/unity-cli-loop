using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

using Debug = UnityEngine.Debug;
using Stopwatch = System.Diagnostics.Stopwatch;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Starts the shared Roslyn compiler worker in the background after a server reset and runs one
    /// small compile against the references the next hot-reload shim compile binds, so that compile
    /// finds the worker running and its references loaded.
    /// </summary>
    internal sealed class SharedRoslynCompilerWorkerWarmUp
    {
        internal const string VibeLogWarmUpComplete = "dynamic_code_shared_worker_warm_up_complete";
        internal const string VibeLogWarmUpSkipped = "dynamic_code_shared_worker_warm_up_skipped";
        internal const string SkipReasonStoppedForTests = "stopped_for_tests";
        internal const string SkipReasonNoHotReload = "no_hot_reload";
        internal const string SkipReasonNoTargets = "no_targets";
        internal const string SkipReasonCompilerUnavailable = "compiler_unavailable";
        internal const string OutcomeFailed = "failed";

        private readonly object _lock = new object();
        private readonly Func<Func<CancellationToken, Task<IReadOnlyList<string>>>> _getReferenceCollector;
        private readonly Func<ExternalCompilerPaths> _resolveCompilerPaths;
        private readonly Func<string> _readPackagePath;
        private readonly Func<IReadOnlyList<string>, ExternalCompilerPaths, Task<SharedWorkerWarmUpOutcome>> _warmWorker;
        private Task _inFlight = Task.CompletedTask;
        private bool _stopped;

        internal SharedRoslynCompilerWorkerWarmUp(
            Func<Func<CancellationToken, Task<IReadOnlyList<string>>>> getReferenceCollector,
            Func<ExternalCompilerPaths> resolveCompilerPaths,
            Func<string> readPackagePath,
            Func<IReadOnlyList<string>, ExternalCompilerPaths, Task<SharedWorkerWarmUpOutcome>> warmWorker)
        {
            Debug.Assert(getReferenceCollector != null, "getReferenceCollector must not be null.");
            Debug.Assert(resolveCompilerPaths != null, "resolveCompilerPaths must not be null.");
            Debug.Assert(readPackagePath != null, "readPackagePath must not be null.");
            Debug.Assert(warmWorker != null, "warmWorker must not be null.");
            _getReferenceCollector = getReferenceCollector;
            _resolveCompilerPaths = resolveCompilerPaths;
            _readPackagePath = readPackagePath;
            _warmWorker = warmWorker;
        }

        internal static SharedRoslynCompilerWorkerWarmUp CreateProduction()
        {
            return new SharedRoslynCompilerWorkerWarmUp(
                () => SharedCompilerWarmUpCoordination.CollectWarmUpReferencePaths,
                ExternalCompilerPathResolver.ResolveWithoutReporting,
                () => UnityCliLoopConstants.PackageResolvedPath,
                SharedRoslynCompilerWorkerHost.WarmUpAsync);
        }

        /// <summary>
        /// Starts a warm-up in the background. Never throws and never runs any of the warm-up
        /// before it returns.
        /// </summary>
        internal void Start()
        {
            lock (_lock)
            {
                if (_stopped)
                {
                    LogSkipped(SkipReasonStoppedForTests);
                    return;
                }

                Task run = RunAsync();
                // Why chain rather than replace: when starts follow each other (a manual server
                // restart), the task a test waits for has to cover the earlier warm-up too.
                // RunAsync never lets an exception out, so this never faults.
                _inFlight = Task.WhenAll(_inFlight, run);
            }
        }

        /// <summary>
        /// Keeps later starts from running and returns every warm-up still in flight.
        /// </summary>
        internal Task StopForTests()
        {
            lock (_lock)
            {
                _stopped = true;
                return _inFlight;
            }
        }

        internal Task GetTaskForTests()
        {
            lock (_lock)
            {
                return _inFlight;
            }
        }

        private async Task RunAsync()
        {
            // Why yield first: Start runs inside the server reset, before the readiness probe
            // awaits; none of the warm-up may run there, and an exception there would fail the
            // server start.
            await Task.Yield();
            Stopwatch total = Stopwatch.StartNew();
            try
            {
                Func<CancellationToken, Task<IReadOnlyList<string>>> collect = _getReferenceCollector();
                if (collect == null)
                {
                    LogSkipped(SkipReasonNoHotReload);
                    return;
                }

                Stopwatch watch = Stopwatch.StartNew();
                IReadOnlyList<string> references = await collect(CancellationToken.None).ConfigureAwait(false);
                long referencesMs = watch.ElapsedMilliseconds;
                if (references.Count == 0)
                {
                    LogSkipped(SkipReasonNoTargets);
                    return;
                }

                await MainThreadSwitcher.SwitchToMainThread();
                watch.Restart();
                // Why read it here: the worker's program source needs the package path, whose first
                // read calls a main-thread-only Unity API, and the worker starts on a pool thread below.
                string packagePath = _readPackagePath();
                Debug.Assert(!string.IsNullOrEmpty(packagePath), "package path must resolve on the main thread.");
                long mainThreadMs = watch.ElapsedMilliseconds;

                // Why a pool thread: the resolver reads only values Unity reads thread-safely, and
                // walks the Editor's folders on every call.
                ExternalCompilerPaths paths = await Task.Run(_resolveCompilerPaths).ConfigureAwait(false);
                if (paths == null)
                {
                    LogSkipped(SkipReasonCompilerUnavailable);
                    return;
                }

                watch.Restart();
                SharedWorkerWarmUpOutcome outcome =
                    await Task.Run(() => _warmWorker(references, paths)).ConfigureAwait(false);
                LogComplete(
                    total.ElapsedMilliseconds,
                    referencesMs,
                    mainThreadMs,
                    watch.ElapsedMilliseconds,
                    references.Count,
                    outcome);
            }
            catch (Exception exception)
            {
                // Why catch everything here: nothing awaits this task outside tests, so an exception
                // would go unseen; it is logged where a developer looks instead of failing silently.
                LogFailed(total.ElapsedMilliseconds, exception);
                Debug.LogException(exception);
            }
        }

        private static void LogSkipped(string reason)
        {
            VibeLogger.LogInfo(
                VibeLogWarmUpSkipped,
                "Shared Roslyn worker warm-up skipped.",
                new { reason });
        }

        private static void LogComplete(
            long totalMs,
            long referencesMs,
            long mainThreadMs,
            long warmMs,
            int referenceCount,
            SharedWorkerWarmUpOutcome outcome)
        {
            VibeLogger.LogInfo(
                VibeLogWarmUpComplete,
                "Shared Roslyn worker warm-up complete.",
                new
                {
                    ms = totalMs,
                    referencesMs,
                    mainThreadMs,
                    warmMs,
                    referenceCount,
                    outcome = outcome.Outcome,
                    failureReason = outcome.FailureReason,
                    errorCount = outcome.ErrorCount
                });
        }

        private static void LogFailed(long totalMs, Exception exception)
        {
            VibeLogger.LogWarning(
                VibeLogWarmUpComplete,
                "Shared Roslyn worker warm-up failed.",
                new
                {
                    ms = totalMs,
                    outcome = OutcomeFailed,
                    failureReason = exception.GetType().Name,
                    exceptionMessage = exception.Message
                });
        }
    }
}
