using System;
using UnityEditor.Compilation;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Represents Unity compiler output before it is shaped into the CLI CompileResponse contract.
    /// </summary>
    public class CompileResult
    {
        /// <summary>
        /// Whether the compilation was successful. Null indicates indeterminate status.
        /// </summary>
        public bool? Success { get; }
        
        /// <summary>
        /// The number of errors.
        /// </summary>
        public int ErrorCount { get; }
        
        /// <summary>
        /// The number of warnings.
        /// </summary>
        public int WarningCount { get; }
        
        /// <summary>
        /// The time of compilation completion.
        /// </summary>
        public DateTime CompletedAt { get; }
        
        /// <summary>
        /// All compiler messages.
        /// </summary>
        public CompilerMessage[] Messages { get; }
        
        /// <summary>
        /// Error messages only.
        /// </summary>
        public CompilerMessage[] Errors { get; }
        
        /// <summary>
        /// Warning messages only.
        /// </summary>
        public CompilerMessage[] Warnings { get; }

        /// <summary>
        /// Whether the compilation result is indeterminate (cannot be determined).
        /// </summary>
        public bool IsIndeterminate { get; }

        /// <summary>
        /// Optional message for additional information
        /// </summary>
        public string Message { get; }

        /// <summary>
        /// Whether force-compile response shaping must keep detailed non-compiler preflight errors.
        /// </summary>
        internal bool PreserveDetailsWhenForceRecompile { get; }

        /// <summary>
        /// Whether this compile declined Unity's Script Updating Consent dialog at least once.
        /// </summary>
        internal bool ApiUpdaterConsentDeclined { get; }

        /// <summary>
        /// Initializes the compilation result.
        /// </summary>
        /// <param name="success">The compilation success flag. Null indicates indeterminate status.</param>
        /// <param name="errorCount">The number of errors.</param>
        /// <param name="warningCount">The number of warnings.</param>
        /// <param name="completedAt">The completion time.</param>
        /// <param name="messages">All messages.</param>
        /// <param name="errors">The error messages.</param>
        /// <param name="warnings">The warning messages.</param>
        /// <param name="isIndeterminate">Whether the result is indeterminate.</param>
        public CompileResult(
            bool? success,
            int errorCount,
            int warningCount,
            DateTime completedAt,
            CompilerMessage[] messages,
            CompilerMessage[] errors,
            CompilerMessage[] warnings,
            bool isIndeterminate = false,
            string message = null,
            bool preserveDetailsWhenForceRecompile = false,
            bool apiUpdaterConsentDeclined = false
        )
        {
            Success = success;
            ErrorCount = errorCount;
            WarningCount = warningCount;
            CompletedAt = completedAt;
            Messages = messages;
            Errors = errors;
            Warnings = warnings;
            IsIndeterminate = isIndeterminate;
            Message = message;
            PreserveDetailsWhenForceRecompile = preserveDetailsWhenForceRecompile;
            ApiUpdaterConsentDeclined = apiUpdaterConsentDeclined;
        }

        /// <summary>
        /// Returns a copy that records a Script Updating Consent decline for response shaping.
        /// </summary>
        internal CompileResult WithApiUpdaterConsentDeclined()
        {
            return new CompileResult(
                Success,
                ErrorCount,
                WarningCount,
                CompletedAt,
                Messages,
                Errors,
                Warnings,
                IsIndeterminate,
                Message,
                PreserveDetailsWhenForceRecompile,
                apiUpdaterConsentDeclined: true);
        }
    }
}
