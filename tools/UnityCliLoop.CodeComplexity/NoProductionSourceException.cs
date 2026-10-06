using System;

namespace UnityCliLoop.CodeComplexity
{
    /// <summary>
    /// Thrown when the scan root has no production C# source to analyze.
    /// </summary>
    public sealed class NoProductionSourceException : Exception
    {
        public NoProductionSourceException(string message)
            : base(message)
        {
        }
    }
}
