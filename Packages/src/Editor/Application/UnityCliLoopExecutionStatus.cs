namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Execution slot state for status reporting, in values that layers outside Domain can read.
    /// The running-tool fields are null while the slot is idle.
    /// </summary>
    internal sealed class UnityCliLoopExecutionStatus
    {
        private UnityCliLoopExecutionStatus(
            bool isBusy,
            string runningToolName,
            int? runningToolElapsedSeconds,
            string runningToolPhase)
        {
            IsBusy = isBusy;
            RunningToolName = runningToolName;
            RunningToolElapsedSeconds = runningToolElapsedSeconds;
            RunningToolPhase = runningToolPhase;
        }

        internal bool IsBusy { get; }

        internal string RunningToolName { get; }

        internal int? RunningToolElapsedSeconds { get; }

        internal string RunningToolPhase { get; }

        internal static UnityCliLoopExecutionStatus Idle()
        {
            return new UnityCliLoopExecutionStatus(false, null, null, null);
        }

        internal static UnityCliLoopExecutionStatus Busy(
            string runningToolName,
            int runningToolElapsedSeconds,
            string runningToolPhase)
        {
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(runningToolName), "runningToolName must not be null or whitespace");
            UnityEngine.Debug.Assert(!string.IsNullOrWhiteSpace(runningToolPhase), "runningToolPhase must not be null or whitespace");

            return new UnityCliLoopExecutionStatus(true, runningToolName, runningToolElapsedSeconds, runningToolPhase);
        }
    }
}
