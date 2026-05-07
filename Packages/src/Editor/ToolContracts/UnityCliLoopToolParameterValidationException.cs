using System;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Exception thrown when tool parameter JSON fails type validation.
    /// </summary>
    public class UnityCliLoopToolParameterValidationException : Exception
    {
        public UnityCliLoopToolParameterValidationException(string message)
            : base(message)
        {
        }

        public UnityCliLoopToolParameterValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}


