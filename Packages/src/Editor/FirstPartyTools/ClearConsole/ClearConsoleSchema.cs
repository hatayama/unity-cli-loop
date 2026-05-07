
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Schema for ClearConsole command parameters
    /// Provides type-safe parameter access for clearing Unity Console
    /// Related classes:
    /// - ConsoleUtility: Service layer for console operations
    /// - ClearConsoleResponse: Type-safe response structure
    /// - ClearConsoleCommand: Command implementation
    /// </summary>
    public class ClearConsoleSchema : UnityCliLoopToolSchema
    {
        /// <summary>
        /// Whether to add a confirmation log message after clearing
        /// </summary>
        public bool AddConfirmationMessage { get; set; } = false;
    }
} 