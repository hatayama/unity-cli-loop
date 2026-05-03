using System;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Thrown when a disabled tool is invoked.
    /// </summary>
    public class ToolDisabledException : Exception
    {
        public string ToolName { get; }

        public ToolDisabledException(string toolName)
            : base($"Tool '{toolName}' is disabled")
        {
            ToolName = toolName;
        }
    }
}
