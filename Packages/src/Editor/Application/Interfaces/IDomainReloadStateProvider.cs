
namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Defines access to Domain Reload State dependencies without exposing their implementation.
    /// </summary>
    public interface IDomainReloadStateProvider
    {
        bool IsDomainReloadInProgress();
    }
}
