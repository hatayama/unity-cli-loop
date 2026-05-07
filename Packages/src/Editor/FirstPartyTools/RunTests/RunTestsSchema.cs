using UnityEditor.TestTools.TestRunner.Api;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Schema for RunTests command parameters
    /// Provides type-safe parameter access with default values
    /// </summary>
    public class RunTestsSchema : UnityCliLoopToolSchema
    {
        /// <summary>
        /// Test mode - EditMode(0), PlayMode(1)
        /// </summary>
        public TestMode TestMode { get; set; } = TestMode.EditMode;

        /// <summary>
        /// Type of test filter - all(0), exact(1), regex(2), assembly(3)
        /// </summary>
        public TestFilterType FilterType { get; set; } = TestFilterType.all;

        /// <summary>
        /// Filter value (specify when filterType is not all)
        /// • exact: Individual test method name (e.g.: io.github.hatayama.UnityCliLoop.ConsoleLogRetrieverTests.GetAllLogs_WithMaskAllOff_StillReturnsAllLogs)
        /// • regex: Class name or namespace (e.g.: io.github.hatayama.UnityCliLoop.ConsoleLogRetrieverTests, io.github.hatayama.UnityCliLoop)
        /// • assembly: Assembly name (e.g.: UnityCliLoop.Tests.Editor)
        /// </summary>
        public string FilterValue { get; set; } = "";
        public bool SaveBeforeRun { get; set; } = false;

    }
} 
