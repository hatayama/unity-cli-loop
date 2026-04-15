using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.uLoopMCP
{
    public interface IDynamicCompilationService
    {
        Task<CompilationResult> CompileAsync(CompilationRequest request, CancellationToken ct = default);
    }
}
