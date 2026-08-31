using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps every foreground warmup entrypoint on the same snippets and request shape.
    internal static class DynamicCodeForegroundWarmupRunner
    {
        internal static async Task<bool> RunForegroundSequenceAsync(
            IDynamicCodeExecutionRuntime runtime,
            bool yieldToForegroundRequests,
            CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(runtime != null, "runtime must not be null");

            // Why: foreground fallback and transport readiness must compile the same source shapes;
            // otherwise one path can report warm while the user's first return-string shape is still cold.
            foreach (string warmupCode in ExecuteDynamicCodeReadinessProbe.CreateReturnStringProbeCodes())
            {
                DynamicCodeExecutionRequest request = CreateRequest(
                    warmupCode,
                    yieldToForegroundRequests);
                ExecutionResult result = await runtime.ExecuteAsync(request, ct).ConfigureAwait(false);
                if (!result.Success)
                {
                    return false;
                }
            }

            return true;
        }

        private static DynamicCodeExecutionRequest CreateRequest(
            string code,
            bool yieldToForegroundRequests)
        {
            return new DynamicCodeExecutionRequest
            {
                Code = code,
                ClassName = DynamicCodeConstants.DEFAULT_CLASS_NAME,
                CompileOnly = false,
                YieldToForegroundRequests = yieldToForegroundRequests
            };
        }
    }
}
