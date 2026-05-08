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
            DynamicCodeSecurityLevel securityLevel,
            bool yieldToForegroundRequests,
            CancellationToken ct)
        {
            System.Diagnostics.Debug.Assert(runtime != null, "runtime must not be null");

            foreach (string warmupCode in DynamicCodeForegroundWarmupSnippets.ReturnStringShapes)
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

            foreach (string warmupCode in DynamicCodeForegroundWarmupSnippets.ReturnStringShapes)
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
