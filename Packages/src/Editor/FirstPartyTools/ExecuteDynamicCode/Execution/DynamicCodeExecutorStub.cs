using System;
using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Fallback stub when no compilation provider is registered.
    /// Returns an error for all execution attempts.
    /// </summary>
    public class DynamicCodeExecutorStub : IDynamicCodeExecutor
    {
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

        public void Dispose()
        {
        }

        private ExecutionResult CreateCompilationProviderUnavailableResult()
        {
            return new ExecutionResult
            {
                Success = false,
                ErrorMessage = "COMPILATION_PROVIDER_UNAVAILABLE: No compilation provider is registered. Check initialization.",
                ExecutionTime = TimeSpan.Zero
            };
        }
    }
}
