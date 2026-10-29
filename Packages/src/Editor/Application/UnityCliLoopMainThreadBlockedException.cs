using System;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Reports that a request stopped waiting for a blocked Editor main thread before any of its tool
    /// code ran, so the protocol layer can answer the waiting CLI with a main_thread_blocked error.
    /// Why not an OperationCanceledException: the request pipeline treats that as a disconnect and
    /// writes no response.
    /// </summary>
    public sealed class UnityCliLoopMainThreadBlockedException : Exception
    {
        public UnityCliLoopMainThreadBlockedException(string toolName, double stallSeconds)
            : base(CreateMessage(toolName, stallSeconds))
        {
            ToolName = toolName;
            StallSeconds = stallSeconds;
        }

        public string ToolName { get; }

        public double StallSeconds { get; }

        private static string CreateMessage(string toolName, double stallSeconds)
        {
            return $"'{toolName}' was not run: the Unity Editor main thread has not run for {stallSeconds:F0}s, so uloop stopped waiting and released the command slot.";
        }
    }
}
