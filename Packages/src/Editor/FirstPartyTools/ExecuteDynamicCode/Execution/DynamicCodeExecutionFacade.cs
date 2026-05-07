using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using Stopwatch = System.Diagnostics.Stopwatch;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides the runtime-facing facade for execute-dynamic-code.
    /// Entry points depend on this facade instead of reaching into factory and executor wiring directly.
    /// </summary>
    internal sealed class DynamicCodeExecutionFacade : IShutdownAwareDynamicCodeExecutionRuntime, IDisposable
    {
        private readonly IDynamicCodeExecutorPool _executorPool;
        private readonly DynamicCodeExecutionScheduler _executionScheduler;

        public DynamicCodeExecutionFacade(IDynamicCodeExecutorPool executorPool)
        {
            _executorPool = executorPool ?? throw new ArgumentNullException(nameof(executorPool));
            _executionScheduler = new DynamicCodeExecutionScheduler(_executorPool.Dispose);
        }

        public async Task<ExecutionResult> ExecuteAsync(
            DynamicCodeExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(request.Code), "request.Code must not be empty");

            return await _executionScheduler.RunForegroundAsync(
                innerCancellationToken => ExecuteCoreAsync(request, innerCancellationToken),
                CreateExecutionInProgressResult,
                cancellationToken);
        }

        public async Task<(bool Entered, ExecutionResult Result)> TryExecuteIfIdleAsync(
            DynamicCodeExecutionRequest request,
            CancellationToken cancellationToken = default)
        {
            Debug.Assert(request != null, "request must not be null");
            Debug.Assert(!string.IsNullOrWhiteSpace(request.Code), "request.Code must not be empty");

            return await _executionScheduler.TryRunIfIdleAsync(
                request.YieldToForegroundRequests,
                innerCancellationToken => ExecuteCoreAsync(request, innerCancellationToken),
                cancellationToken);
        }

        private async Task<ExecutionResult> ExecuteCoreAsync(
            DynamicCodeExecutionRequest request,
            CancellationToken cancellationToken)
        {
            Stopwatch executorAcquireStopwatch = Stopwatch.StartNew();
            IDynamicCodeExecutor executor = _executorPool.GetOrCreate(request.SecurityLevel);
            executorAcquireStopwatch.Stop();

            Stopwatch executorTotalStopwatch = Stopwatch.StartNew();
            ExecutionResult result = await executor.ExecuteCodeAsync(
                request.Code,
                request.ClassName,
                request.Parameters,
                cancellationToken,
                request.CompileOnly);
            executorTotalStopwatch.Stop();

            if (result.Timings == null)
            {
                result.Timings = new System.Collections.Generic.List<string>();
            }

            result.Timings.Add($"[Perf] ExecutorAcquire: {executorAcquireStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            result.Timings.Add($"[Perf] ExecutorTotal: {executorTotalStopwatch.Elapsed.TotalMilliseconds:F1}ms");
            return result;
        }

        private static ExecutionResult CreateExecutionInProgressResult()
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = UnityCliLoopConstants.ERROR_MESSAGE_EXECUTION_IN_PROGRESS
            };
        }

        public void Dispose()
        {
            _executionScheduler.Dispose();
        }

        public Task ShutdownAsync()
        {
            return _executionScheduler.ShutdownAsync();
        }
    }
}
