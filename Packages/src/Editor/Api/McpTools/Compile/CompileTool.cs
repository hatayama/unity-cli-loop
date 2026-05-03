using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop
{
    /// <summary>
    /// Compile tool handler - Type-safe implementation using Schema and Response
    /// Handles Unity project compilation with optional force recompile
    /// Related classes: CompileUseCase, CompilationStateValidationService, CompilationExecutionService
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - UseCase + Tool Pattern (DDD Integration)
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