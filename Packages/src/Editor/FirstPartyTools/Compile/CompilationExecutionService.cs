using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compilation execution service
    /// Single function: Execute Unity project compilation
    /// Related classes: CompileController, CompileUseCase, CompileTool
    /// </summary>
    public class CompilationExecutionService
    {
        /// <summary>
        /// Execute compilation asynchronously
        /// </summary>
        /// <param name="request">Compile request with force and delayed-result settings.</param>
        /// <returns>Compilation result</returns>
        public async Task<CompileResult> ExecuteCompilationAsync(UnityCliLoopCompileRequest request, CancellationToken ct)
        {
            if (request == null)
            {
                throw new System.ArgumentNullException(nameof(request));
            }

            using CompileController compileController = new();
            compileController.SetResultRecordingContext(CompileResultRecordingContext.Create(request));
            return await compileController.TryCompileAsync(request.ForceRecompile, ct);
        }
    }
}
