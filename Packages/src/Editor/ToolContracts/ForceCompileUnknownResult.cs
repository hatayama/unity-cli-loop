namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Shared result values for forced full compilation when Unity does not provide a definitive result.
    /// </summary>
    public sealed class ForceCompileUnknownResult
    {
        public const string MessageText = "Forced full compilation was triggered, but Unity did not provide a definitive result after domain reload.";
        public const string ErrorCodeText = "COMPILE_RESULT_UNKNOWN";
        public const string NextActionText = "Wait for domain reload to complete, then run `uloop compile` without --force-recompile to obtain a definitive result.";

        private ForceCompileUnknownResult()
        {
            Success = false;
            ErrorCount = null;
            WarningCount = null;
            Message = MessageText;
        }

        public bool Success { get; }
        public int? ErrorCount { get; }
        public int? WarningCount { get; }
        public string Message { get; }

        public static ForceCompileUnknownResult Create()
        {
            return new ForceCompileUnknownResult();
        }
    }
}
