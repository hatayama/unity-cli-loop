using System;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Exception thrown when tool parameter JSON fails type validation.
    /// </summary>
    public class ParameterValidationException : Exception
    {
        public ParameterValidationException(string message)
            : base(message)
        {
        }

        public ParameterValidationException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}


