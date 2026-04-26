using System.ComponentModel;
using UnityEditor.TestTools.TestRunner.Api;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Supported test filter types
    /// </summary>
    public enum TestFilterType
    {
        all = 0,
        exact = 1,
        regex = 2,
        assembly = 3
    }

    /// <summary>
    /// Schema for RunTests command parameters
    /// Provides type-safe parameter access with default values
    /// </summary>
    public class RunTestsSchema : BaseToolSchema
    {
        /// <summary>
        /// Test mode - EditMode(0), PlayMode(1)
        /// </summary>
        [Description("Test mode - EditMode(0), PlayMode(1)")]
        public TestMode TestMode { get; set; } = TestMode.EditMode;

        /// <summary>
        /// Type of test filter - all(0), exact(1), regex(2), assembly(3)
        /// </summary>
        [Description("Type of test filter - all(0), exact(1), regex(2), assembly(3)")]
        public TestFilterType FilterType { get; set; } = TestFilterType.all;

        /// <summary>
        /// Filter value (specify when filterType is not all)
        /// • exact: Individual test method name (e.g.: io.github.hatayama.uLoopMCP.ConsoleLogRetrieverTests.GetAllLogs_WithMaskAllOff_StillReturnsAllLogs)
        /// • regex: Class name or namespace (e.g.: io.github.hatayama.uLoopMCP.ConsoleLogRetrieverTests, io.github.hatayama.uLoopMCP)
        /// • assembly: Assembly name (e.g.: uLoopMCP.Tests.Editor)
        /// </summary>
        [Description("Filter value (specify when filterType is not all)\n• exact: Individual test method name (e.g.: io.github.hatayama.uLoopMCP.ConsoleLogRetrieverTests.GetAllLogs_WithMaskAllOff_StillReturnsAllLogs)\n• regex: Class name or namespace (e.g.: io.github.hatayama.uLoopMCP.ConsoleLogRetrieverTests, io.github.hatayama.uLoopMCP)\n• assembly: Assembly name (e.g.: uLoopMCP.Tests.Editor)")]
        public string FilterValue { get; set; } = "";

        [Description("Save unsaved loaded Scene changes and current Prefab Stage changes before running tests")]
        public bool SaveBeforeRun { get; set; } = false;

    }
} 
