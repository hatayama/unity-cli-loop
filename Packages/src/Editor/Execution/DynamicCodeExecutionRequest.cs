namespace io.github.hatayama.uLoopMCP
{
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
