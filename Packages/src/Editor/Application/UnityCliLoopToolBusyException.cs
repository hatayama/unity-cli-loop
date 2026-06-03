using System;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Carries both tool names so the protocol layer can produce a stable server_busy payload.
    /// </summary>
    public sealed class UnityCliLoopToolBusyException : Exception
    {
        public UnityCliLoopToolBusyException(
            string runningToolName,
            string requestedToolName,
            bool isPlaying = false,
            bool isPaused = false)
            : base(CreateMessage(runningToolName, requestedToolName))
        {
            RunningToolName = runningToolName;
            RequestedToolName = requestedToolName;
            IsPlaying = isPlaying;
            IsPaused = isPaused;
        }

        public string RunningToolName { get; }

        public string RequestedToolName { get; }

        public bool IsPlaying { get; }

        public bool IsPaused { get; }

        private static string CreateMessage(string runningToolName, string requestedToolName)
        {
            return $"Unity is busy running '{runningToolName}'. Retry '{requestedToolName}' after the running tool completes.";
        }
    }
}
