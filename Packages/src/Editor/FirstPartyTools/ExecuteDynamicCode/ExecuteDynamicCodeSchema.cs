using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Parameter schema for dynamic code execution tool
    /// Related classes: ExecuteDynamicCodeTool, ExecuteDynamicCodeResponse
    /// </summary>
    public class ExecuteDynamicCodeSchema : UnityCliLoopToolSchema
    {
        /// <summary>C# code to execute</summary>
        public string Code { get; set; } = "";
        
        /// <summary>Runtime parameters (advanced; usually unnecessary)</summary>
        public Dictionary<string, object> Parameters { get; set; } = new();
        
        /// <summary>Compile only (do not execute)</summary>
        public bool CompileOnly { get; set; } = false;

        public bool YieldToForegroundRequests { get; set; } = false;
    }
}
