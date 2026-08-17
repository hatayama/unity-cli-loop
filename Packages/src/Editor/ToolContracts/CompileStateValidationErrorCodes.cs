namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Machine-readable error codes for compile state validation failures.
    /// </summary>
    public sealed class CompileStateValidationErrorCodes
    {
        public const string AlreadyInProgressErrorCodeText = "COMPILE_ALREADY_IN_PROGRESS";
        public const string EditorUpdatingErrorCodeText = "COMPILE_EDITOR_UPDATING";

        private CompileStateValidationErrorCodes()
        {
        }
    }
}
