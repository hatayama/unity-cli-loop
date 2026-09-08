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
        // The subscription is injected rather than taken directly on AssemblyReloadEvents so a
        // test can fire the handler it registered; a real domain reload cannot be driven from one.
        public static void Initialize(
            IPausePointPersistenceStore store,
            Action<AssemblyReloadEvents.AssemblyReloadCallback> subscribeBeforeReload)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            if (subscribeBeforeReload == null)
            {
                throw new ArgumentNullException(nameof(subscribeBeforeReload));
            }

            subscribeBeforeReload(() => SnapshotNow(store));
        }

        // Why filtered against the registry rather than by the ledger alone: a pause point the
        // user already cleared, or one whose capture window ran out, must not come back after the
        // reload. IsArmedAfterExpiry rather than IsArmed because expiry is lazy: without a status
        // poll an elapsed marker still reads as enabled, and the re-arm would hand it a fresh
        // full timeout it never earned.
        internal static void SnapshotNow(IPausePointPersistenceStore store)
        {
            if (store == null)
            {
                throw new ArgumentNullException(nameof(store));
            }

            store.Save(PausePointPersistRequestLedger.CollectArmed(UloopPausePointRegistry.IsArmedAfterExpiry));
        }
    }
}
