using System.Collections.Generic;
using Newtonsoft.Json;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Response for dynamic code execution tool

    /// Related classes: ExecuteDynamicCodeTool, ExecuteDynamicCodeSchema
    /// </summary>
    public class ExecuteDynamicCodeResponse : UnityCliLoopToolResponse, IUnityCliLoopTimingResponse
    {
        /// <summary>Execution result</summary>
        public string Result { get; set; }
        
        /// <summary>Log messages</summary>
        public List<string> Logs { get; set; } = new();
        
        /// <summary>Compilation errors</summary>
        public List<CompilationErrorDto> CompilationErrors { get; set; } = new();
        
        /// <summary>Error message (on failure)</summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Error message alias for ErrorMessage.
        /// Why keep: documented tool-response JSON field (Skill/SKILL.md); agents and CLI read it.
        /// </summary>
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
        /// Why: the native CLI needs an explicit Unity-side reload signal before it can safely wait.
        /// </summary>
        [JsonProperty("DomainReloadWaitRequired")]
        public bool DomainReloadWaitRequired { get; set; } = false;

        /// <summary>
        /// Optional recovery steps when execution cannot complete automatically.
        /// </summary>
        public string[] NextActions { get; set; }

        /// <summary>
        /// Whether Play Mode is running as this response is returned. Always serialized so a
        /// stopped versus playing Editor is visible even when EditorPaused is omitted.
        /// </summary>
        public bool EditorPlaying { get; set; }

        /// <summary>
        /// Whether the Editor is paused as this response is returned. Lets an agent recognize a
        /// post-interrupt state (e.g. a pause point hit during this execution) instead of mistaking
        /// stale-looking results for a bug.
        /// </summary>
        public bool EditorPaused { get; set; } = false;

        /// <summary>
        /// The pause point id responsible for the current pause, when EditorPaused is caused by a
        /// pause-point hit. Empty when the Editor is not paused, or is paused for an unrelated reason.
        /// </summary>
        public string ActivePausePointId { get; set; } = string.Empty;

        /// <summary>
        /// Lightweight internal timings for benchmark comparison.
        /// </summary>
        [JsonProperty("Timings")]
        public List<string> Timings { get; set; } = new();

        public bool EmitTimingsInJsonResponse { get; set; } = false;

        bool IUnityCliLoopTimingResponse.EmitsTimingsInJsonResponse => EmitTimingsInJsonResponse;

        public void AddTiming(string timing)
        {
            if (Timings == null)
            {
                Timings = new List<string>();
            }

            Timings.Add(timing);
        }

        // Keep timings available in memory for diagnostics and readiness decisions
        // while avoiding noisy default payloads for normal execute-dynamic-code users.
        public bool ShouldSerializeTimings()
        {
            return EmitTimingsInJsonResponse && Timings != null && Timings.Count > 0;
        }

        public bool ShouldSerializeEmitTimingsInJsonResponse()
        {
            return false;
        }

        public bool ShouldSerializeDomainReloadWaitRequired()
        {
            return DomainReloadWaitRequired;
        }

        public bool ShouldSerializeNextActions()
        {
            return NextActions != null && NextActions.Length > 0;
        }

        public bool ShouldSerializeEditorPaused()
        {
            return EditorPaused;
        }

        public bool ShouldSerializeActivePausePointId()
        {
            return !string.IsNullOrEmpty(ActivePausePointId);
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
