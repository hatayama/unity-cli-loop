using UnityEditor;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Application service responsible for security validation before server operations.
    /// Handles editor state validation and security checks.
    /// 
    /// Related classes:
    /// - UnityEditor.EditorApplication: Provides editor state information
    /// - McpPortValidator: Validates port security
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

        /// <summary>
        /// Validates port security settings.
        /// </summary>
        /// <param name="port">Port number to validate</param>
        /// <returns>Validation result with security details</returns>
        public ValidationResult ValidatePortSecurity(int port)
        {
            if (!McpPortValidator.ValidatePort(port, "for Unity CLI bridge"))
            {
                return new ValidationResult(false, 
                    $"Port number must be between 1 and 65535. Received: {port}");
            }

            return new ValidationResult(true);
        }
    }
}
