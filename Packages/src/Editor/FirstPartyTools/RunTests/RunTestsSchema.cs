using System.ComponentModel;

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
        public UnityCliLoopTestMode TestMode { get; set; } = UnityCliLoopTestMode.EditMode;

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

        /// <summary>
        /// How to handle unsaved Scene and Prefab Stage changes before running tests.
        /// </summary>
        public RunTestsUnsavedChangesMode UnsavedChanges { get; set; } = RunTestsUnsavedChangesMode.save;

        /// <summary>
        /// Maximum seconds to wait for Unity Test Runner RunFinished before canceling the await.
        /// Kept below the CLI absolute response timeout so hung runs free the single-flight slot first.
        /// </summary>
        public int TimeoutSeconds { get; set; } = RunTestsExecutionTimeout.DefaultTimeoutSeconds;

        /// <summary>
        /// When true and TestMode is PlayMode, keeps the project's Enter Play Mode settings instead of forcing Domain Reload off; a Domain Reload during the run is survived by storing the result in SessionState for CLI polling. Ignored for EditMode.
        /// </summary>
        public bool RespectEnterPlayModeSettings { get; set; } = false;

        /// <summary>
        /// Internal request identifier used for delayed result recovery across domain reload.
        /// </summary>
        [Browsable(false)]
        public string RequestId { get; set; } = "";
    }
} 
