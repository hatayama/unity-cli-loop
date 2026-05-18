using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    /// <summary>
    /// Compile tool handler - Type-safe implementation using Schema and Response
    /// Handles Unity project compilation with optional force recompile
    /// Related classes: CompileUseCase, CompilationStateValidationService, CompilationExecutionService
    /// </summary>
    [McpTool(Description = "Execute Unity project compilation")]
    public class CompileTool : AbstractUnityTool<CompileSchema, CompileResponse>
    {
        public override string ToolName => "compile";

        /// <summary>
        /// Execute compile tool - delegates to CompileUseCase
        /// </summary>
        /// <param name="parameters">Type-safe parameters</param>
        /// <param name="cancellationToken">Cancellation token for timeout control</param>
        /// <returns>Compile result</returns>
        protected override async Task<CompileResponse> ExecuteAsync(CompileSchema parameters, CancellationToken cancellationToken)
        {
            // Create and execute CompileUseCase instance
            CompileUseCase useCase = new();
            return await useCase.ExecuteAsync(parameters, cancellationToken);
        }
    }
}