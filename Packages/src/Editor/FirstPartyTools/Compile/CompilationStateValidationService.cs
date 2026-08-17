using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compilation state validation service
    /// Single function: Validate state before compilation execution
    /// Related classes: CompileTool, CompileUseCase, CompileSessionState
    /// </summary>
    public class CompilationStateValidationService
    {
        /// <summary>
        /// Validate state before compilation execution
        /// </summary>
        /// <returns>Validation result</returns>
        public ValidationResult ValidateCompilationState()
        {
            if (EditorApplication.isCompiling)
            {
                return ValidationResult.FailureWithErrorCode(
                    "Compilation is already in progress. Please wait for the current compilation to finish.",
                    CompileStateValidationErrorCodes.AlreadyInProgressErrorCodeText
                );
            }
            if (EditorApplication.isUpdating)
            {
                return ValidationResult.FailureWithErrorCode(
                    "Cannot compile while editor is updating. Please wait for the update to complete.",
                    CompileStateValidationErrorCodes.EditorUpdatingErrorCodeText
                );
            }

            return ValidationResult.Success();
        }
    }
}
