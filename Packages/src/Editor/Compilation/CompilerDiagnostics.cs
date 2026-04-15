using System;
using System.Collections.Generic;
using UnityEditor.Compilation;

namespace io.github.hatayama.uLoopMCP
{
    internal sealed class CompilerDiagnostics
    {
        public List<CompilationError> Errors { get; }

        public List<string> Warnings { get; }

        public bool HasAmbiguityErrors { get; }

        private CompilerDiagnostics(
            List<CompilationError> errors,
            List<string> warnings,
            bool hasAmbiguityErrors)
        {
            Errors = errors;
            Warnings = warnings;
            HasAmbiguityErrors = hasAmbiguityErrors;
        }

        public static CompilerDiagnostics FromMessages(CompilerMessage[] messages)
        {
            List<CompilationError> errors = new List<CompilationError>();
            List<string> warnings = new List<string>();
            bool hasAmbiguityErrors = false;

            foreach (CompilerMessage message in messages)
            {
                if (message.type == CompilerMessageType.Warning)
                {
                    warnings.Add(message.message);
                    continue;
                }

                if (message.type != CompilerMessageType.Error)
                {
                    continue;
                }

                string errorCode = ExtractErrorCode(message.message);
                if (errorCode == "CS0104" || errorCode == "CS0234")
                {
                    hasAmbiguityErrors = true;
                }

                errors.Add(new CompilationError
                {
                    Message = message.message,
                    ErrorCode = errorCode,
                    Line = message.line,
                    Column = message.column
                });
            }

            return new CompilerDiagnostics(errors, warnings, hasAmbiguityErrors);
        }

        private static string ExtractErrorCode(string message)
        {
            int csIndex = message.IndexOf("CS", StringComparison.Ordinal);
            if (csIndex >= 0 && csIndex + 6 <= message.Length)
            {
                string candidate = message.Substring(csIndex, 6);
                if (candidate.Length == 6 && candidate[2] >= '0' && candidate[2] <= '9')
                {
                    return candidate;
                }
            }

            return "UNKNOWN";
        }
    }
}
