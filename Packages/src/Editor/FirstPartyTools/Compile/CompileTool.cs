using System.Threading;
using System.Threading.Tasks;

using io.github.hatayama.UnityCliLoop.Domain;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compile tool handler - Type-safe implementation using Schema and Response
    /// Handles Unity project compilation with optional force recompile
    /// Related classes: CompileUseCase, CompilationStateValidationService, CompilationExecutionService
    /// </summary>
    [UnityCliLoopTool]
    public class CompileTool : UnityCliLoopTool<CompileSchema, CompileResponse>
    {
        public override string ToolName => UnityCliLoopConstants.TOOL_NAME_COMPILE;

        protected override async Task<CompileResponse> ExecuteAsync(CompileSchema parameters, CancellationToken ct)
        {
            // Why: CompileTool is created via Activator.CreateInstance by the tool registry and cannot
            // receive CompositionRoot-owned services through its own constructor, so the shared
            // session state service is fetched from the facade here and threaded through the pipeline.
            UnityCliLoopEditorSessionStateService sessionStateService =
                UnityCliLoopEditorSessionStateFacade.Service;
            CompileUseCase useCase = new(sessionStateService);
            return await useCase.CompileAsync(parameters, ct);
        }
    }
}
