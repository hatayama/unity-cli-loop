using System.ComponentModel;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Schema for GetToolDetails tool parameters
    /// Provides type-safe parameter access for retrieving tool information
    /// </summary>
    public class GetToolDetailsSchema : BaseToolSchema
    {
        /// <summary>
        /// Whether to include development-only tools in the results
        /// </summary>
        [Description("Whether to include development-only tools in the results")]
        public bool IncludeDevelopmentOnly { get; set; } = false;
    }
} 