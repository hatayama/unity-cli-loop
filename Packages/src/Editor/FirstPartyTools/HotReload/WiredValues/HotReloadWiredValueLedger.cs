using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The last value wired into each added field of each identifiable host, kept for as long as
    /// the domain lives.
    /// </summary>
    internal sealed class HotReloadWiredValueLedger
    {
        private readonly Dictionary<HotReloadWiredValueHostKey, HotReloadWiredValueDescriptor> _entries =
            new Dictionary<HotReloadWiredValueHostKey, HotReloadWiredValueDescriptor>();

        private readonly HashSet<HotReloadWiredValueHostKey> _wiredWhilePlaying =
            new HashSet<HotReloadWiredValueHostKey>();

        internal int Count => _entries.Count;

        /// <summary>
        /// Remembers <paramref name="descriptor"/>, replacing what was wired into the same field of
        /// the same host before, and whether it was wired while Play Mode ran. Wiring the key again
        /// outside Play Mode drops that mark, since the host is then known to be in the Edit-time
        /// scene.
        /// </summary>
        internal void Record(
            HotReloadWiredValueHostKey key, HotReloadWiredValueDescriptor descriptor, bool wiredWhilePlaying)
        {
            Debug.Assert(descriptor != null, "descriptor must not be null.");
            _entries[key] = descriptor;
            if (wiredWhilePlaying)
            {
                _wiredWhilePlaying.Add(key);
                return;
            }

            _wiredWhilePlaying.Remove(key);
        }

        internal bool WasWiredWhilePlaying(HotReloadWiredValueHostKey key)
        {
            return _wiredWhilePlaying.Contains(key);
        }

        /// <summary>
        /// Stops treating a key as wired while Play Mode ran, keeping its value.
        /// </summary>
        internal void ClearPlayMark(HotReloadWiredValueHostKey key)
        {
            _wiredWhilePlaying.Remove(key);
        }

        internal void Remove(HotReloadWiredValueHostKey key)
        {
            _entries.Remove(key);
            _wiredWhilePlaying.Remove(key);
        }

        internal bool TryGet(HotReloadWiredValueHostKey key, out HotReloadWiredValueDescriptor descriptor)
        {
            return _entries.TryGetValue(key, out descriptor);
        }

        /// <summary>
        /// Whether any host has a value wired into <paramref name="storeFieldKey"/>.
        /// </summary>
        internal bool HasAnyForField(string storeFieldKey)
        {
            foreach (HotReloadWiredValueHostKey key in _entries.Keys)
            {
                if (string.Equals(key.StoreFieldKey, storeFieldKey, StringComparison.Ordinal))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// A copy of every recorded key, so a caller can inspect hosts after releasing its lock.
        /// </summary>
        internal List<HotReloadWiredValueHostKey> SnapshotKeys()
        {
            return new List<HotReloadWiredValueHostKey>(_entries.Keys);
        }

        internal void Clear()
        {
            _entries.Clear();
            _wiredWhilePlaying.Clear();
        }
    }
}
