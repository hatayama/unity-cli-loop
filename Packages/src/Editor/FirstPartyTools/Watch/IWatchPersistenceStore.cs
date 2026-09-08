using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Stores the watch expressions that must be re-registered after a domain reload.
    /// </summary>
    internal interface IWatchPersistenceStore
    {
        IReadOnlyList<WatchPersistedRecord> Load();

        void Save(IReadOnlyList<WatchPersistedRecord> records);
    }
}
