using System;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.FirstPartyTools.Factory;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Keeps the registered Dynamic Code Services entries for lookup by the owning module.
    /// </summary>
    internal sealed class DynamicCodeServicesRegistry
    {
        private const int StartupPrewarmDelayFrameCount = 1;
        private readonly object _serverScopedServicesLock = new();
        private Task _serverScopedDrainTask = Task.CompletedTask;
        private IDynamicCodeExecutionRuntime _runtimeFacade;
        private DynamicCodeStartupPrewarmer _startupPrewarmer;

        private readonly Lazy<IDynamicCodeSourcePreparationService> _sourcePreparationServiceValue;

        internal IDynamicCodeSourcePreparationService SourcePreparationService => _sourcePreparationServiceValue.Value;

        internal CompiledCommandEntryPointResolver CommandEntryPointResolver { get; } =
            new CompiledCommandEntryPointResolver();

        private readonly Lazy<RegistryDynamicCodeExecutorFactory> _executorFactoryValue;

        internal DynamicCodeServicesRegistry()
        {
            _sourcePreparationServiceValue = new Lazy<IDynamicCodeSourcePreparationService>(
                () => new DynamicCodeSourcePreparationService());
            _executorFactoryValue = new Lazy<RegistryDynamicCodeExecutorFactory>(
                () => new RegistryDynamicCodeExecutorFactory(
                    new DynamicCodeCompilationServiceFactory(),
                    SourcePreparationService,
                    CommandEntryPointResolver));
        }

        internal RegistryDynamicCodeExecutorFactory ExecutorFactory => _executorFactoryValue.Value;

        internal IExecuteDynamicCodeUseCase GetExecuteDynamicCodeUseCase()
        {
            IDynamicCodeExecutionRuntime runtimeFacade = GetRuntimeFacade();
            return new ExecuteDynamicCodeUseCase(runtimeFacade);
        }

        internal void RequestStartupPrewarm()
        {
            GetStartupPrewarmer().Request();
        }

        internal void ResetServerScopedServices()
        {
            IDynamicCodeExecutionRuntime runtimeFacade;

            lock (_serverScopedServicesLock)
            {
                runtimeFacade = _runtimeFacade;
                _runtimeFacade = null;
                _startupPrewarmer = null;
                _serverScopedDrainTask = ChainDrainTask(
                    _serverScopedDrainTask,
                    ShutdownRuntimeAsync(runtimeFacade));
            }
        }

        private IDynamicCodeExecutionRuntime GetRuntimeFacade()
        {
            lock (_serverScopedServicesLock)
            {
                if (_runtimeFacade == null)
                {
                    IDynamicCodeExecutorPool executorPool = new DynamicCodeExecutorPool(ExecutorFactory);
                    _runtimeFacade = new DynamicCodeExecutionFacade(executorPool);
                }

                return _runtimeFacade;
            }
        }

        private DynamicCodeStartupPrewarmer GetStartupPrewarmer()
        {
            lock (_serverScopedServicesLock)
            {
                if (_startupPrewarmer == null)
                {
                    _startupPrewarmer = new DynamicCodeStartupPrewarmer(
                        GetRuntimeFacade(),
                        StartupPrewarmDelayFrameCount);
                }

                return _startupPrewarmer;
            }
        }

        private static Task ShutdownRuntimeAsync(IDynamicCodeExecutionRuntime runtimeFacade)
        {
            SharedRoslynCompilerWorkerHost.ShutdownForServerReset();

            if (runtimeFacade is IShutdownAwareDynamicCodeExecutionRuntime shutdownAwareRuntime)
            {
                return shutdownAwareRuntime.ShutdownAsync();
            }

            if (runtimeFacade is IDisposable disposableRuntime)
            {
                disposableRuntime.Dispose();
            }

            return Task.CompletedTask;
        }

        private static Task ChainDrainTask(Task currentDrainTask, Task nextDrainTask)
        {
            Task observedCurrentDrainTask = ObserveDrainTask(currentDrainTask);
            Task observedNextDrainTask = ObserveDrainTask(nextDrainTask);
            if (observedCurrentDrainTask.IsCompleted)
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

        private static Task ObserveDrainTask(Task drainTask)
        {
            if (drainTask == null || drainTask.IsCompletedSuccessfully)
            {
                return Task.CompletedTask;
            }

            return drainTask.ContinueWith(
                task => LogDrainFailure(task),
                System.Threading.CancellationToken.None,
                TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
        }

        private static void LogDrainFailure(Task drainTask)
        {
            if (drainTask.IsFaulted)
            {
                Exception exception = drainTask.Exception?.InnerException ?? drainTask.Exception;
                VibeLogger.LogWarning(
                    "dynamic_code_runtime_shutdown_failed",
                    "Dynamic code runtime shutdown failed; continuing with a fresh runtime",
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
                    "dynamic_code_runtime_shutdown_cancelled",
                    "Dynamic code runtime shutdown was cancelled; continuing with a fresh runtime");
            }
        }
    }

    /// <summary>
    /// Provides Dynamic Code Services behavior for Unity CLI Loop.
    /// </summary>
    internal static class DynamicCodeServices
    {
        private static readonly DynamicCodeServicesRegistry RegistryValue = new DynamicCodeServicesRegistry();

        internal static DynamicCodeServicesRegistry GetRegistry()
        {
            return RegistryValue;
        }

        internal static IDynamicCodeSourcePreparationService SourcePreparationService
        {
            get { return GetRegistry().SourcePreparationService; }
        }

        internal static CompiledCommandEntryPointResolver CommandEntryPointResolver
        {
            get { return GetRegistry().CommandEntryPointResolver; }
        }

        internal static RegistryDynamicCodeExecutorFactory ExecutorFactory
        {
            get { return GetRegistry().ExecutorFactory; }
        }

        internal static IExecuteDynamicCodeUseCase GetExecuteDynamicCodeUseCase()
        {
            return GetRegistry().GetExecuteDynamicCodeUseCase();
        }

        internal static void RequestStartupPrewarm()
        {
            GetRegistry().RequestStartupPrewarm();
        }

        internal static void ResetServerScopedServices()
        {
            GetRegistry().ResetServerScopedServices();
        }
    }
}
