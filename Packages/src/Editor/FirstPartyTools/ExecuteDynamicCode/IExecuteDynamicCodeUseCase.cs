using System.Threading;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Execute Dynamic Code use case boundary consumed by the owning tool.
    /// </summary>
    internal interface IExecuteDynamicCodeUseCase
    {
        Task<ExecuteDynamicCodeResponse> ExecuteAsync(
            ExecuteDynamicCodeSchema parameters,
            CancellationToken cancellationToken);
    }
}
