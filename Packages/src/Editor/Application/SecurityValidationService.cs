using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Application
{
    // Port for validating Unity Editor state before server operations.
    /// <summary>
    /// Defines the Security Validation operations required by the owning workflow.
    /// </summary>
    public interface ISecurityValidationService
    {
        ValidationResult ValidateEditorState();
    }
}
