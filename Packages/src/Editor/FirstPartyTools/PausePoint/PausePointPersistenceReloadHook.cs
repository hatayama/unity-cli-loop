using System;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Writes the still-armed persisted enable requests to the store in the last moment before a
    /// domain reload destroys the ledger.
    /// </summary>
    internal static class PausePointPersistenceReloadHook
    {
        public static void Initialize(IPausePointPersistenceStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            AssemblyReloadEvents.beforeAssemblyReload += () => SnapshotNow(store);
        }

        // Why filtered by IsArmed rather than by the ledger alone: a pause point the user already
        // cleared, or one that expired, must not come back after the reload.
        internal static void SnapshotNow(IPausePointPersistenceStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            store.Save(PausePointPersistRequestLedger.CollectArmed(UloopPausePointRegistry.IsArmed));
        }
    }
}
