using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Application service responsible for editor state validation before server operations.
    /// </summary>
    public class SecurityValidationService
    {
        /// <summary>
        /// Validates that the Unity Editor is in a safe state for server operations.
        /// </summary>
        /// <returns>Validation result with error details if invalid</returns>
        public ValidationResult ValidateEditorState()
        {
            if (EditorApplication.isCompiling)
            {
                return new ValidationResult(false, 
                    "Cannot start Unity CLI bridge while Unity is compiling. Please wait for compilation to complete.");
            }

            return new ValidationResult(true);
        }
    }
}
