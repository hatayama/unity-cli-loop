using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compilation state validation service
    /// Single function: Validate state before compilation execution
    /// Related classes: CompileTool, CompileUseCase, CompileSessionState
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
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
                return ValidationResult.Failure(
                    "Compilation is already in progress. Please wait for the current compilation to finish."
                );
            }
            if (EditorApplication.isUpdating)
            {
                return ValidationResult.Failure(
                    "Cannot compile while editor is updating. Please wait for the update to complete."
                );
            }

            AssemblyDefinitionDuplicationValidationService asmdefValidationService = new();
            ValidationResult asmdefValidation = asmdefValidationService.ValidateNoDuplicateAsmdefNamesFromConsoleErrors();
            if (!asmdefValidation.IsValid)
            {
                return asmdefValidation;
            }
            
            return ValidationResult.Success();
        }
    }
}
