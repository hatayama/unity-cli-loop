using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Carries the request data needed for Dynamic Code Execution behavior.
    /// </summary>
    internal sealed class DynamicCodeExecutionRequest
    {
        public string Code { get; set; }

        public string ClassName { get; set; } = DynamicCodeConstants.DEFAULT_CLASS_NAME;

        public object[] Parameters { get; set; }

        public bool CompileOnly { get; set; }

        public DynamicCodeSecurityLevel SecurityLevel { get; set; }

        public bool YieldToForegroundRequests { get; set; }
    }
}
