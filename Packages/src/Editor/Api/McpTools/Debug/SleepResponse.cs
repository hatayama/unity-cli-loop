namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Response for debug sleep tool execution
    /// Related classes:
    /// - SleepTool: Implementation that generates this response
    /// - SleepSchema: Input parameters for the sleep tool
    /// - BaseToolResponse: Base class providing timing information
    /// </summary>
    public class SleepResponse : BaseToolResponse
    {
        /// <summary>
        /// Message indicating the result of the sleep operation
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// Actual number of seconds the tool slept before completion or timeout
        /// </summary>
        public int ActualSleepSeconds { get; set; }

        /// <summary>
        /// Indicates whether the operation completed successfully or was cancelled
        /// </summary>
        public bool WasCancelled { get; set; }
    }
}