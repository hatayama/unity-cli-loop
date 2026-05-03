using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Compilation state validation service
    /// Single function: Validate state before compilation execution
    /// Related classes: CompileTool, CompileUseCase, McpSessionManager
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
            
            if (McpEditorSettings.GetIsDomainReloadInProgress())
            {
                return ValidationResult.Failure(
                    "Cannot compile while domain reload is in progress. Please wait for the domain reload to complete."
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