using System;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.Factory;

namespace io.github.hatayama.UnityCliLoop
{
    internal static class DynamicCodeServices
    {
        private static readonly object ServerScopedServicesLock = new();
        private static TimeSpan s_serverScopedDrainTimeout = TimeSpan.FromSeconds(5);
        private static Task _serverScopedDrainTask = Task.CompletedTask;
        private static CancellationTokenSource _serverScopedLifetimeCancellationTokenSource;
        private static IDynamicCodeExecutorPool _executorPool;
        private static IDynamicCodeExecutionRuntime _runtimeFacade;
        private static IPrewarmDynamicCodeUseCase _prewarmDynamicCodeUseCase;

        public static DynamicCodeSourcePreparationService SourcePreparationService { get; } =
            new DynamicCodeSourcePreparationService();

        public static ExternalCompilerPathResolutionService ExternalCompilerPathResolver { get; } =
            new ExternalCompilerPathResolutionService();

        public static DynamicReferenceSetBuilderService ReferenceSetBuilder { get; } =
            new DynamicReferenceSetBuilderService();

        public static IDynamicCompilationPlanner CompilationPlanner { get; } =
            new DynamicCompilationPlanner(SourcePreparationService);

        public static ICompiledAssemblyLoader AssemblyLoadService { get; } =
            new CompiledAssemblyLoadService();

        public static DynamicCompilationBackend CompilationBackend { get; } =
            new DynamicCompilationBackend();

        public static ICompiledAssemblyBuilder AssemblyBuilder { get; } =
            new CompiledAssemblyBuilder(
                ExternalCompilerPathResolver,
                ReferenceSetBuilder,
                CompilationBackend);

        public static CompiledCommandEntryPointResolver CommandEntryPointResolver { get; } =
            new CompiledCommandEntryPointResolver();

        public static RegistryDynamicCodeExecutorFactory ExecutorFactory { get; } =
            new RegistryDynamicCodeExecutorFactory(
                SourcePreparationService,
                CommandEntryPointResolver);

        public static async Task<IExecuteDynamicCodeUseCase> GetExecuteDynamicCodeUseCaseAsync()
        {
            IDynamicCodeExecutionRuntime runtimeFacade = await GetRuntimeFacadeAsync();
            return new ExecuteDynamicCodeUseCase(runtimeFacade);
        }

        public static async Task<IPrewarmDynamicCodeUseCase> GetPrewarmDynamicCodeUseCaseAsync(
            string serverStartingLockToken = null)
        {
            await EnsureServerScopedServicesInitializedAsync(serverStartingLockToken);

            lock (ServerScopedServicesLock)
            {
                return _prewarmDynamicCodeUseCase;
            }
        }

        public static void ResetServerScopedServices()
        {
            CancellationTokenSource lifetimeCancellationTokenSource;
            IDynamicCodeExecutionRuntime runtimeFacade;
            Task shutdownTask;

            lock (ServerScopedServicesLock)
            {
                lifetimeCancellationTokenSource = _serverScopedLifetimeCancellationTokenSource;
                runtimeFacade = _runtimeFacade;

                _serverScopedLifetimeCancellationTokenSource = null;
                _executorPool = null;
                _runtimeFacade = null;
                _prewarmDynamicCodeUseCase = null;

                shutdownTask = CreateShutdownTask(lifetimeCancellationTokenSource, runtimeFacade);
                _serverScopedDrainTask = ChainDrainTask(_serverScopedDrainTask, shutdownTask);
            }
        }

        private static async Task<IDynamicCodeExecutionRuntime> GetRuntimeFacadeAsync()
        {
            await EnsureServerScopedServicesInitializedAsync();

            lock (ServerScopedServicesLock)
            {
                return _runtimeFacade;
            }
        }

        private static async Task EnsureServerScopedServicesInitializedAsync(
            string serverStartingLockToken = null)
        {
            DynamicCodeCompilationServiceRegistration.EnsureRegistered();

            Task drainTask;

            lock (ServerScopedServicesLock)
            {
                if (_runtimeFacade != null)
                {
                    AttachServerStartingLockTokenIfNeeded(serverStartingLockToken);
                    return;
                }

                drainTask = _serverScopedDrainTask;
            }

            await AwaitDrainTaskAsync(drainTask);

            lock (ServerScopedServicesLock)
            {
                if (_runtimeFacade != null)
                {
                    AttachServerStartingLockTokenIfNeeded(serverStartingLockToken);
                    return;
                }

                _serverScopedLifetimeCancellationTokenSource = new CancellationTokenSource();
                _executorPool = new DynamicCodeExecutorPool(ExecutorFactory);
                _runtimeFacade = new DynamicCodeExecutionFacade(
                    AssemblyBuilder,
                    _executorPool);
                _prewarmDynamicCodeUseCase = new PrewarmDynamicCodeUseCase(
                    _runtimeFacade,
                    _serverScopedLifetimeCancellationTokenSource.Token,
                    null,
                    serverStartingLockToken);
            }
        }

        private static void AttachServerStartingLockTokenIfNeeded(string serverStartingLockToken)
        {
            if (string.IsNullOrEmpty(serverStartingLockToken))
            {
                return;
            }

            if (_prewarmDynamicCodeUseCase is PrewarmDynamicCodeUseCase prewarmDynamicCodeUseCase)
            {
                prewarmDynamicCodeUseCase.AttachServerStartingLockToken(serverStartingLockToken);
            }
        }

        private static Task CreateShutdownTask(
            CancellationTokenSource lifetimeCancellationTokenSource,
            IDynamicCodeExecutionRuntime runtimeFacade)
        {
            if (lifetimeCancellationTokenSource == null && runtimeFacade == null)
            {
                return Task.CompletedTask;
            }

            return ShutdownServerScopedServicesAsync(lifetimeCancellationTokenSource, runtimeFacade);
        }

        private static Task ChainDrainTask(Task currentDrainTask, Task nextDrainTask)
        {
            Task observedCurrentDrainTask = CreateObservedDrainTask(
                currentDrainTask,
                "server_scoped_shutdown_previous_failed");
            Task observedNextDrainTask = CreateObservedDrainTask(
                nextDrainTask,
                "server_scoped_shutdown_failed");

            if (observedCurrentDrainTask.IsCompletedSuccessfully)
            {
                return observedNextDrainTask;
            }

            return ContinueAfterDrainAsync(observedCurrentDrainTask, observedNextDrainTask);
        }

        private static async Task ContinueAfterDrainAsync(Task currentDrainTask, Task nextDrainTask)
        {
            await currentDrainTask;
            await nextDrainTask;
        }

        private static Task CreateObservedDrainTask(Task drainTask, string operation)
        {
            if (drainTask == null || drainTask.IsCompletedSuccessfully)
            {
                return Task.CompletedTask;
            }

            return drainTask.ContinueWith(
                task => LogDrainFailure(operation, task),
                CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void LogDrainFailure(string operation, Task drainTask)
        {
            if (drainTask.IsFaulted)
            {
                Exception exception = drainTask.Exception?.InnerException ?? drainTask.Exception;
                VibeLogger.LogWarning(
                    operation,
                    "Server-scoped dynamic code shutdown failed; continuing with a fresh runtime",
                    new
                    {
                        exception_type = exception?.GetType().Name,
                        exception_message = exception?.Message
                    });
                return;
            }

            if (drainTask.IsCanceled)
            {
                VibeLogger.LogInfo(
                    operation,
                    "Server-scoped dynamic code shutdown was cancelled; continuing with a fresh runtime");
            }
        }

        internal static async Task AwaitDrainTaskAsync(Task drainTask)
        {
            if (drainTask == null)
            {
                return;
            }

            if (drainTask.IsCompleted)
            {
                await drainTask;
                return;
            }

            TimeSpan timeout = s_serverScopedDrainTimeout;
            UnityEngine.Debug.Assert(timeout > TimeSpan.Zero, "server-scoped drain timeout must be positive");

            Task completedTask = await Task.WhenAny(drainTask, Task.Delay(timeout));
            if (completedTask == drainTask)
            {
                await drainTask;
                return;
            }

            VibeLogger.LogWarning(
                "server_scoped_shutdown_timeout",
                "Server-scoped dynamic code shutdown exceeded the drain timeout; continuing with a fresh runtime",
                new
                {
                    timeout_ms = (int)timeout.TotalMilliseconds
                });
        }

        private static async Task ShutdownServerScopedServicesAsync(
            CancellationTokenSource lifetimeCancellationTokenSource,
            IDynamicCodeExecutionRuntime runtimeFacade)
        {
            lifetimeCancellationTokenSource?.Cancel();
            SharedRoslynCompilerWorkerHost.ShutdownForServerReset();

            if (runtimeFacade is IShutdownAwareDynamicCodeExecutionRuntime shutdownAwareRuntime)
            {
                await shutdownAwareRuntime.ShutdownAsync();
            }
            else
            {
                (runtimeFacade as IDisposable)?.Dispose();
            }

            lifetimeCancellationTokenSource?.Dispose();
        }

        internal static TimeSpan SwapDrainTimeoutForTests(TimeSpan timeout)
        {
            UnityEngine.Debug.Assert(timeout > TimeSpan.Zero, "timeout must be positive");

            TimeSpan previous = s_serverScopedDrainTimeout;
            s_serverScopedDrainTimeout = timeout;
            return previous;
        }

        internal static void SetServerScopedServicesForTests(
            CancellationTokenSource lifetimeCancellationTokenSource,
            IDynamicCodeExecutorPool executorPool,
            IDynamicCodeExecutionRuntime runtimeFacade,
            IPrewarmDynamicCodeUseCase prewarmDynamicCodeUseCase)
        {
            lock (ServerScopedServicesLock)
            {
                _serverScopedLifetimeCancellationTokenSource = lifetimeCancellationTokenSource;
                _executorPool = executorPool;
                _runtimeFacade = runtimeFacade;
                _prewarmDynamicCodeUseCase = prewarmDynamicCodeUseCase;
            }
        }

        internal static Task GetServerScopedDrainTaskForTests()
        {
            lock (ServerScopedServicesLock)
            {
                return _serverScopedDrainTask;
            }
        }

        internal static void ResetStateForTests()
        {
            lock (ServerScopedServicesLock)
            {
                _serverScopedLifetimeCancellationTokenSource = null;
                _executorPool = null;
                _runtimeFacade = null;
                _prewarmDynamicCodeUseCase = null;
                _serverScopedDrainTask = Task.CompletedTask;
            }
        }
    }
}
