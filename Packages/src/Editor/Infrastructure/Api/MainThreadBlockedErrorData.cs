namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Tells the CLI that its command stopped waiting for a blocked Editor main thread before any of
    /// the tool's code ran, so the CLI can report a safe-to-retry failure instead of a silent wait.
    /// </summary>
    public class MainThreadBlockedErrorData : JsonRpcErrorData
    {
        public override string type => JsonRpcErrorTypes.MainThreadBlocked;

        public string runningToolName { get; }

        public double stallSeconds { get; }

        public MainThreadBlockedErrorData(string runningToolName, double stallSeconds, string message)
            : base(message)
        {
            this.runningToolName = runningToolName;
            this.stallSeconds = stallSeconds;
        }
    }
}
