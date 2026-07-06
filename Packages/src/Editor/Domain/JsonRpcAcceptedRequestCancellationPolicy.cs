using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Domain
{
    /// <summary>
    /// Decides whether accepted JSON-RPC work should be canceled when the CLI disconnects.
    /// </summary>
    public static class JsonRpcAcceptedRequestCancellationPolicy
    {
        public static bool ShouldCancelOnClientDisconnect(
            string methodName,
            bool? compileWaitsForDomainReload)
        {
            if (methodName != UnityCliLoopConstants.TOOL_NAME_COMPILE)
            {
                return true;
            }

            return !CompileRequestWaitsForDomainReload(compileWaitsForDomainReload);
        }

        private static bool CompileRequestWaitsForDomainReload(bool? compileWaitsForDomainReload)
        {
            return compileWaitsForDomainReload ?? true;
        }
    }
}
