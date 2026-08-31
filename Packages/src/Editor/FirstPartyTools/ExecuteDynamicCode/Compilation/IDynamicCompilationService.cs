using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Dynamic Compilation operations required by the owning workflow.
    /// </summary>
    public interface IDynamicCompilationService
    {
        Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default);
    }
}
