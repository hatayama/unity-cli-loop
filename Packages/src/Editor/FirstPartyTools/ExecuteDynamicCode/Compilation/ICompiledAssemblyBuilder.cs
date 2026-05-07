using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Compiled Assembly contract used by Unity CLI Loop.
    /// </summary>
    public interface ICompiledAssemblyBuilder
    {
        Task<CompiledAssemblyBuildResult> BuildAsync(
            DynamicCompilationPlan plan,
            CancellationToken ct = default);
    }
}
