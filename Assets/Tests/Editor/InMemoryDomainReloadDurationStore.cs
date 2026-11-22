using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Keeps domain reload measurements in memory so tests never touch the live Editor's SessionState.
    /// </summary>
    internal sealed class InMemoryDomainReloadDurationStore : IDomainReloadDurationStore
    {
        private DomainReloadDurationState _state = DomainReloadDurationState.Empty;

        public DomainReloadDurationState Load()
        {
            return _state;
        }

        public void Save(DomainReloadDurationState state)
        {
            _state = state;
        }
    }
}
