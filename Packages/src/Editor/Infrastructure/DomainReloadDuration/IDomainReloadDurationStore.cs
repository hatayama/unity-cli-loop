namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Keeps the domain reload measurements across the reloads of one Editor session.
    /// </summary>
    internal interface IDomainReloadDurationStore
    {
        /// <summary>
        /// Returns the saved measurements, or <see cref="DomainReloadDurationState.Empty"/> when none were saved.
        /// </summary>
        DomainReloadDurationState Load();

        void Save(DomainReloadDurationState state);
    }
}
