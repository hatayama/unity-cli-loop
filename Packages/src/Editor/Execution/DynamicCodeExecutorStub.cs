using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Fallback stub when no compilation provider is registered.
    /// Returns an error for all execution attempts.
    /// </summary>
    public class DynamicCodeExecutorStub : IDynamicCodeExecutor
    {
        private readonly ExecutionStatistics _statistics;

        public DynamicCodeExecutorStub()
        {
            _statistics = new ExecutionStatistics();
        }

        /// <summary>Execute code asynchronously (always returns a Roslyn required error)</summary>
        public Task<ExecutionResult> ExecuteCodeAsync(
            string code,
            string className = DynamicCodeConstants.DEFAULT_CLASS_NAME,
            object[] parameters = null,
            CancellationToken cancellationToken = default,
            bool compileOnly = false)
        {
            return Task.FromResult(CreateCompilationProviderUnavailableResult());
        }

        /// <summary>Retrieve execution statistics</summary>
        public ExecutionStatistics GetStatistics()
        {
            return new ExecutionStatistics
            {
                TotalExecutions = _statistics.TotalExecutions,
                SuccessfulExecutions = _statistics.SuccessfulExecutions,
                FailedExecutions = _statistics.FailedExecutions,
                AverageExecutionTime = _statistics.AverageExecutionTime,
                SecurityViolations = _statistics.SecurityViolations,
                CompilationErrors = _statistics.CompilationErrors
            };
        }

        public void Dispose()
        {
        }

        private ExecutionResult CreateCompilationProviderUnavailableResult()
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = "COMPILATION_PROVIDER_UNAVAILABLE: No compilation provider is registered. Check initialization.",
                ExecutionTime = TimeSpan.Zero,
                Statistics = _statistics
            };
        }
    }
}
