using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Response for dynamic code execution tool

    /// Related classes: ExecuteDynamicCodeTool, ExecuteDynamicCodeSchema
    /// </summary>
    public class ExecuteDynamicCodeResponse : BaseToolResponse
    {
        private const bool EmitTimingsInJsonResponses = false;

        /// <summary>Execution success flag</summary>
        public bool Success { get; set; }
        
        /// <summary>Execution result</summary>
        public string Result { get; set; }
        
        /// <summary>Log messages</summary>
        public List<string> Logs { get; set; } = new();
        
        /// <summary>Compilation errors</summary>
        public List<CompilationErrorDto> CompilationErrors { get; set; } = new();
        
        /// <summary>Error message (on failure)</summary>
        public string ErrorMessage { get; set; }
        
        /// <summary>Current security level</summary>
        public string SecurityLevel { get; set; }
        
        /// <summary>Error message (alias for ErrorMessage)</summary>
        public string Error 
        { 
            get => ErrorMessage; 
            set => ErrorMessage = value; 
        }
        
        /// <summary>
        /// Code formatted for compilation
        /// (After extracting/moving using statements and applying class/method wrapping)
        /// Allows checking the actual compiled code during debugging
        /// </summary>
        public string UpdatedCode { get; set; }

        /// <summary>
        /// Summary of diagnostics (unique count, total count, first error brief)
        /// </summary>
        public string DiagnosticsSummary { get; set; }

        /// <summary>
        /// Structured diagnostics for rich clients (line/column/code/message/hint/suggestions)
        /// </summary>
        public List<CompilationErrorDto> Diagnostics { get; set; } = new();

        /// <summary>
        /// Lightweight internal timings for benchmark comparison.
        /// </summary>
        public List<string> Timings { get; set; } = new();

        // Keep timings available in memory for diagnostics and readiness decisions
        // while avoiding noisy default payloads for normal execute-dynamic-code users.
        public bool ShouldSerializeTimings()
        {
            return EmitTimingsInJsonResponses;
        }
    }
    
    /// <summary>
    /// DTO for compilation error information
    /// </summary>
    public class CompilationErrorDto
    {
        /// <summary>Error message</summary>
        public string Message { get; set; }
        
        /// <summary>Line number</summary>
        public int Line { get; set; }
        
        /// <summary>Column number</summary>
        public int Column { get; set; }
        
        /// <summary>Compiler error code (e.g., CS0103)</summary>
        public string ErrorCode { get; set; }

        /// <summary>Optional hint for resolving the error</summary>
        public string Hint { get; set; }

        /// <summary>Suggested fixes (e.g., add using or qualify)</summary>
        public List<string> Suggestions { get; set; } = new();

        /// <summary>Context lines around the error with a caret pointer</summary>
        public string Context { get; set; }

        /// <summary>Pointer column for caret rendering (1-based)</summary>
        public int PointerColumn { get; set; }
    }
}
