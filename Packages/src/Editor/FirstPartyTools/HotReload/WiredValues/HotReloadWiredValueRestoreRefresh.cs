using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Re-checks every wired value whose host is at its place now by reading its slot once, so a
    /// status or apply response shows the restore instead of the reason that held before.
    /// </summary>
    internal sealed class HotReloadWiredValueRestoreRefresh
    {
        private readonly HotReloadWiredValuePersistence _persistence;
        private readonly IHotReloadWiredValueResolver _resolver;
        private readonly IHotReloadWiredValueSlotReader _reader;

        internal HotReloadWiredValueRestoreRefresh(
            HotReloadWiredValuePersistence persistence,
            IHotReloadWiredValueResolver resolver,
            IHotReloadWiredValueSlotReader reader)
        {
            Debug.Assert(persistence != null, "persistence must not be null.");
            Debug.Assert(resolver != null, "resolver must not be null.");
            Debug.Assert(reader != null, "reader must not be null.");
            _persistence = persistence;
            _resolver = resolver;
            _reader = reader;
        }

        /// <summary>
        /// Reads the slot of every ledger entry whose host is at its place now and returns how many
        /// were read. Does nothing off the main thread, where no host can be found.
        /// </summary>
        internal int Run()
        {
            if (!_resolver.IsMainThread)
            {
                return 0;
            }

            // First, because a pending slot retries only when the generation moved since its last attempt.
            _persistence.NoteHostsMayHaveChanged();
            int reads = 0;
            foreach (HotReloadWiredValueHostKey key in _persistence.SnapshotLedgerKeys())
            {
                // The host-missing row is still true, so it stays as it is.
                if (_resolver.IsHostMissing(key.Identity, false))
                {
                    continue;
                }

                if (!_resolver.TryResolveHost(key.Identity, out object host))
                {
                    continue;
                }

                int restoredBefore = _persistence.RestoredCount;
                bool read = _reader.TryRead(host, key.StoreFieldKey);
                // TryRestore counts a value the field's type then rejects, and this read leaves no
                // slot to stop the next refresh from counting it again.
                if (!read && _persistence.RestoredCount > restoredBefore)
                {
                    _persistence.NoteRestoredValueUnreadable(key);
                }

                reads++;
            }

            return reads;
        }
    }
}
