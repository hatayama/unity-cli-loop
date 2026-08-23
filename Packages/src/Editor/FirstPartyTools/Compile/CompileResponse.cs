using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compile error or warning information
    /// </summary>
    public class CompileIssue
    {
        public string Message { get; set; }
        public string File { get; set; }
        public int Line { get; set; }

        public CompileIssue(string message, string file, int line)
        {
            Message = message;
            File = file;
            Line = line;
        }

        public CompileIssue() { }
    }

    /// <summary>
    /// Response schema for Compile command
    /// Provides type-safe response structure
    /// </summary>
    public class CompileResponse : UnityCliLoopToolResponse
    {
        /// <summary>
        /// Number of compilation errors.
        /// Null is used when the tool intentionally does not provide details (e.g., forced recompile),
        /// because Unity reports errors/warnings after domain reload and clients should fetch logs later.
        /// </summary>
        public int? ErrorCount { get; set; }

        /// <summary>
        /// Number of compilation warnings.
        /// Null is used when the tool intentionally does not provide details (e.g., forced recompile),
        /// because Unity reports errors/warnings after domain reload and clients should fetch logs later.
        /// </summary>
        public int? WarningCount { get; set; }

        /// <summary>
        /// Compilation errors.
        /// Null is used when the tool intentionally does not provide details (e.g., forced recompile),
        /// because Unity reports errors/warnings after domain reload and clients should fetch logs later.
        /// </summary>
        public CompileIssue[] Errors { get; set; }

        /// <summary>
        /// Compilation warnings.
        /// Null is used when the tool intentionally does not provide details (e.g., forced recompile),
        /// because Unity reports errors/warnings after domain reload and clients should fetch logs later.
        /// </summary>
        public CompileIssue[] Warnings { get; set; }

        /// <summary>
        /// Optional message for additional information
        /// </summary>
        public string Message { get; set; }

        public string ErrorCode { get; set; }

        /// <summary>
        /// Optional recovery steps when compile cannot proceed automatically.
        /// </summary>
        public string[] NextActions { get; set; }

        /// <summary>
        /// Unity project root path (from UnityEngine.Application.dataPath).
        /// Set only when WaitForDomainReload=true so the CLI can report which project produced the result.
        /// </summary>
        public string ProjectRoot { get; set; }

        /// <summary>
        /// Optional warning about a condition compile does not block on, e.g. Play Mode being
        /// active when compile was requested so the domain reload discards Play session state.
        /// </summary>
        public string Warning { get; set; }

        /// <summary>
        /// Create a new CompileResponse
        /// </summary>
        public CompileResponse(
            bool success,
            int? errorCount,
            int? warningCount,
            CompileIssue[] errors,
            CompileIssue[] warnings,
            string message = null
        )
        {
            Success = success;
            ErrorCount = errorCount;
            WarningCount = warningCount;
            Errors = errors;
            Warnings = warnings;
            Message = message;
        }

        /// <summary>
        /// Parameterless constructor for JSON deserialization
        /// </summary>
        public CompileResponse()
        {
        }
    }
}
