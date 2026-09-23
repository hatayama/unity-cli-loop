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
            bool? isPlaying = null,
            bool? isPaused = null,
            bool? isCompiling = null,
            bool? isUpdating = null,
            int? runningToolElapsedSeconds = null,
            string runningToolPhase = null)
            : base(CreateMessage(runningToolName, requestedToolName))
        {
            RunningToolName = runningToolName;
            RequestedToolName = requestedToolName;
            IsPlaying = isPlaying;
            IsPaused = isPaused;
            IsCompiling = isCompiling;
            IsUpdating = isUpdating;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
            RunningToolPhase = runningToolPhase;
        }

        public string RunningToolName { get; }

        public string RequestedToolName { get; }

        public bool? IsPlaying { get; }

        public bool? IsPaused { get; }

        public bool? IsCompiling { get; }

        public bool? IsUpdating { get; }

        public int? RunningToolElapsedSeconds { get; }

        // Null when the rejection did not come from the single-flight slot, e.g. an Editor-state busy.
        public string RunningToolPhase { get; }

        private static string CreateMessage(string runningToolName, string requestedToolName)
        {
            return $"Unity is busy running '{runningToolName}'. Retry '{requestedToolName}' after the running tool completes.";
        }
    }
}
