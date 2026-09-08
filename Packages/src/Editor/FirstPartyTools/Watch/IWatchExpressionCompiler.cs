using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Compiles one watch expression into an evaluator. Exists so restore can be driven without
    /// the real Roslyn compilation pipeline.
    /// </summary>
    public interface IWatchExpressionCompiler
    {
        Task<WatchCompilationResult> CompileAsync(string expression, CancellationToken ct);
    }
}
