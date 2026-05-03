using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Schema for debug sleep tool parameters
    /// Related classes:
    /// - SleepTool: Implementation of the debug sleep tool
    /// - SleepResponse: Response structure for the sleep tool
    /// </summary>
    public class SleepSchema : BaseToolSchema
    {
        /// <summary>
        /// Number of seconds to sleep for testing purposes (default: 15)
        /// </summary>
        [Description("Number of seconds to sleep for testing purposes (default: 15)")]
        public int SleepSeconds { get; set; } = 15;
    }
}