namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Machine-readable error codes for compile requests that Unity accepted but could not carry through.
    /// </summary>
    public static class CompileLifecycleErrorCodes
    {
        /// <summary>
        /// Unity reported isCompiling for the request but never raised compilationStarted within the
        /// pending-start timeout, so the compile is still queued inside the Editor.
        /// </summary>
        public const string CompileNotStartedErrorCodeText = "COMPILE_NOT_STARTED";
    }
}
