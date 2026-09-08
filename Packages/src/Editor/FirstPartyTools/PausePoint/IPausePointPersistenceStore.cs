using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Storage for the enable requests that must be replayed after a domain reload.
    /// </summary>
    internal interface IPausePointPersistenceStore
    {
        IReadOnlyList<PausePointPersistedRecord> Load();

        void Save(IReadOnlyList<PausePointPersistedRecord> records);
    }
}
