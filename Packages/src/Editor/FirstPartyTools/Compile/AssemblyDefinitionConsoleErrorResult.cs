using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Immutable result for Assembly Definition and Assembly Reference Console error detection.
    /// </summary>
    public sealed class AssemblyDefinitionConsoleErrorResult
    {
        public AssemblyDefinitionConsoleError[] Errors { get; }
        public bool HasErrors => Errors.Length > 0;
        public string Message { get; }

        public AssemblyDefinitionConsoleErrorResult(AssemblyDefinitionConsoleError[] errors)
        {
            Errors = errors ?? Array.Empty<AssemblyDefinitionConsoleError>();
            Message = HasErrors
                ? AssemblyDefinitionConsoleErrorMessageFormatter.CreateFailureMessage(Errors)
                : null;
        }
    }
}
