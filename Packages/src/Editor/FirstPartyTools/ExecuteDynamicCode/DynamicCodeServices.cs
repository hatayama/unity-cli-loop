using System;
using System.Threading;
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
        private readonly object _serverScopedServicesLock = new();
        private Task _serverScopedDrainTask = Task.CompletedTask;
        private IDynamicCodeExecutionRuntime _runtimeFacade;

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

        internal void ResetServerScopedServices()
        {
            IDynamicCodeExecutionRuntime runtimeFacade;

            lock (_serverScopedServicesLock)
            {
                runtimeFacade = _runtimeFacade;
                _runtimeFacade = null;
                _serverScopedDrainTask = ChainDrainTask(
                    _serverScopedDrainTask,
                    ShutdownRuntimeAsync(runtimeFacade));
            }
        }

        internal void ResetServerScopedServicesBeforeDomainReload()
        {
            IDynamicCodeExecutionRuntime runtimeFacade;

            lock (_serverScopedServicesLock)
            {
                runtimeFacade = _runtimeFacade;
                _runtimeFacade = null;
                _serverScopedDrainTask = Task.CompletedTask;
            }

            SignalRuntimeShutdownBeforeDomainReload(runtimeFacade);
            SharedRoslynCompilerWorkerHost.ShutdownForServerReset();
        }

        internal void SetRuntimeFacadeForTests(IDynamicCodeExecutionRuntime runtimeFacade)
        {
            lock (_serverScopedServicesLock)
            {
                _runtimeFacade = runtimeFacade;
                _serverScopedDrainTask = Task.CompletedTask;
            }
        }

        internal Task GetServerScopedDrainTaskForTests()
        {
            lock (_serverScopedServicesLock)
            {
                return _serverScopedDrainTask;
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

        private static void SignalRuntimeShutdownBeforeDomainReload(IDynamicCodeExecutionRuntime runtimeFacade)
        {
            if (runtimeFacade is IShutdownAwareDynamicCodeExecutionRuntime shutdownAwareRuntime)
            {
                Task shutdownTask = shutdownAwareRuntime.ShutdownAsync();
                _ = ObserveDrainTask(shutdownTask);
                return;
            }

            if (runtimeFacade is IDisposable disposableRuntime)
            {
                disposableRuntime.Dispose();
            }
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
            await currentDrainTask.ConfigureAwait(false);
            await nextDrainTask.ConfigureAwait(false);
        }

        private static Task ObserveDrainTask(Task drainTask)
        {
            if (drainTask == null || drainTask.IsCompletedSuccessfully)
            {
                return Task.CompletedTask;
            }

            return drainTask.ContinueWith(
                task => LogDrainFailure(task),
                CancellationToken.None,
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

        internal static CompiledCommandEntryPointResolver CommandEntryPointResolver
        {
            get { return GetRegistry().CommandEntryPointResolver; }
        }

        internal static IExecuteDynamicCodeUseCase GetExecuteDynamicCodeUseCase()
        {
            return GetRegistry().GetExecuteDynamicCodeUseCase();
        }

        internal static void ResetServerScopedServices()
        {
            GetRegistry().ResetServerScopedServices();
        }

        internal static void ResetServerScopedServicesBeforeDomainReload()
        {
            GetRegistry().ResetServerScopedServicesBeforeDomainReload();
        }
    }
}
