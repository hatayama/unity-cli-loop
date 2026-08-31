using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Proves that the project IPC bridge can answer a real request before external callers see it as ready.
    /// </summary>
    public interface IUnityCliLoopServerReadinessProbe
    {
        Task ProbeAsync(CancellationToken ct);
    }
}
