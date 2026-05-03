using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// ClearConsole tool handler - Type-safe implementation using Schema and Response
    /// Clears Unity console logs for clean development workflow
    /// Related classes: ClearConsoleUseCase, ConsoleUtility, ClearConsoleSchema, ClearConsoleResponse
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
    /// </summary>
    [McpTool(Description = "Clear Unity console logs")]
    public class ClearConsoleTool : AbstractUnityTool<ClearConsoleSchema, ClearConsoleResponse>
    {
        public override string ToolName => "clear-console";

        /// <summary>
        /// Execute console clear tool
        /// </summary>
        /// <param name="parameters">Type-safe parameters</param>
        /// <param name="cancellationToken">Cancellation token for timeout control</param>
        /// <returns>Clear operation result</returns>
        protected override async Task<ClearConsoleResponse> ExecuteAsync(ClearConsoleSchema parameters, CancellationToken cancellationToken)
        {
            // Create and execute ClearConsoleUseCase instance
            ClearConsoleUseCase useCase = new();
            return await useCase.ExecuteAsync(parameters, cancellationToken);
        }
    }
} 