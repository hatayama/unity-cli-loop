using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.ToolContracts;
using io.github.hatayama.UnityCliLoop.Domain;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Keeps every foreground warmup entrypoint on the same snippets and request shape.
    internal static class DynamicCodeForegroundWarmupRunner
    {
        internal static async Task<bool> RunForegroundSequenceAsync(
            IDynamicCodeExecutionRuntime runtime,
            DynamicCodeSecurityLevel securityLevel,
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
                    securityLevel,
                    yieldToForegroundRequests);
                ExecutionResult result = await runtime.ExecuteAsync(request, ct);
                if (!result.Success)
                {
                    return false;
                }
            }

            return true;
        }

        internal static async Task<bool> TryRunBackgroundSequenceAsync(
            IDynamicCodeExecutionRuntime runtime,
            DynamicCodeSecurityLevel securityLevel,
            bool yieldToForegroundRequests,
            CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(runtime != null, "runtime must not be null");

            // Why: background probes must match the foreground sequence so whichever path succeeds
            // first marks the same execution shape as ready.
            foreach (string warmupCode in ExecuteDynamicCodeReadinessProbe.CreateReturnStringProbeCodes())
            {
                DynamicCodeExecutionRequest request = CreateRequest(
                    warmupCode,
                    securityLevel,
                    yieldToForegroundRequests);
                (bool entered, ExecutionResult result) = await runtime.TryExecuteIfIdleAsync(request, ct);
                if (!entered || !result.Success)
                {
                    return false;
                }
            }

            return true;
        }

        private static DynamicCodeExecutionRequest CreateRequest(
            string code,
            DynamicCodeSecurityLevel securityLevel,
            bool yieldToForegroundRequests)
        {
            return new DynamicCodeExecutionRequest
            {
                Code = code,
                ClassName = DynamicCodeConstants.DEFAULT_CLASS_NAME,
                CompileOnly = false,
                SecurityLevel = securityLevel,
                YieldToForegroundRequests = yieldToForegroundRequests
            };
        }
    }
}
