using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.uLoopMCP
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
        /// <param name="forceRecompile">Force recompile flag</param>
        /// <returns>Compilation result</returns>
        public async Task<CompileResult> ExecuteCompilationAsync(bool forceRecompile, CancellationToken ct)
        {
            using CompileController compileController = new();
            return await compileController.TryCompileAsync(forceRecompile, ct);
        }
    }
}
