using System.Threading.Tasks;
using System.Threading;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compilation execution service
    /// Single function: Execute Unity project compilation
    /// Related classes: CompileController, CompileUseCase, CompileTool
    /// Design reference: @Packages/docs/ARCHITECTURE_Unity.md - Application Service Layer (Single Function Implementation)
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
