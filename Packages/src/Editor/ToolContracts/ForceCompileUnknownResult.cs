namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Shared result values for forced full compilation when Unity does not provide a definitive result.
    /// </summary>
    public sealed class ForceCompileUnknownResult
    {
        public const string MessageText = "Forced full compilation completed. Unity does not return a definitive compile result for this forced full compile path, so fields that Unity did not provide are intentionally null; run get-logs to inspect the compiler output.";

        private ForceCompileUnknownResult(bool? success)
        {
            Success = success;
            ErrorCount = null;
            WarningCount = null;
            Message = MessageText;
        }

        public bool? Success { get; }
        public int? ErrorCount { get; }
        public int? WarningCount { get; }
        public string Message { get; }

        public static ForceCompileUnknownResult Create(bool? success)
        {
            return new ForceCompileUnknownResult(success);
        }
    }
}
