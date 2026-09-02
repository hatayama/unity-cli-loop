namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Test mode selector shared between the CLI schema and the run-tests pipeline.
    /// </summary>
    public enum UnityCliLoopTestMode
    {
        EditMode = 0,
        PlayMode = 1
    }

    /// <summary>
    /// Filter strategy that determines how the run-tests filter value is interpreted.
    /// </summary>
    public enum TestFilterType
    {
        all = 0,
        exact = 1,
        regex = 2,
        assembly = 3,
        // Why the verbatim identifier: the CLI spells the option value "class", and the schema
        // generator emits enum member names verbatim, so the C# keyword has to be escaped here.
        @class = 4
    }

    /// <summary>
    /// Defines machine-readable run-tests result states returned to CLI callers.
    /// </summary>
    internal static class RunTestsExecutionStatus
    {
        public const string Passed = "Passed";
        public const string Failed = "Failed";
        public const string NoTestsFound = "NoTestsFound";
        public const string ExecutionFailed = "ExecutionFailed";
    }
}
