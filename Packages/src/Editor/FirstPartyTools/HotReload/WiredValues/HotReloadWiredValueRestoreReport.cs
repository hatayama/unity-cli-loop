using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What restoring wired values achieved since the current scene reload session began: how
    /// many came back, and which did not.
    /// </summary>
    internal sealed class HotReloadWiredValueRestoreReport
    {
        private readonly List<HotReloadWiredValueRestoreFailure> _failures =
            new List<HotReloadWiredValueRestoreFailure>();

        private int _reportedFailureCount;

        internal int RestoredCount { get; private set; }

        internal IReadOnlyList<HotReloadWiredValueRestoreFailure> Failures => _failures;

        internal void AddRestored()
        {
            RestoredCount++;
        }

        internal void AddFailure(string hostIdentity, string storeFieldKey, string reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(reason), "reason must not be empty.");
            _failures.Add(new HotReloadWiredValueRestoreFailure(hostIdentity, storeFieldKey, reason));
        }

        /// <summary>
        /// Drops the failure of this host and field, for a value that came back after all.
        /// </summary>
        internal void RemoveFailure(string hostIdentity, string storeFieldKey)
        {
            int index = _failures.FindIndex(failure =>
                string.Equals(failure.HostIdentity, hostIdentity, StringComparison.Ordinal)
                && string.Equals(failure.StoreFieldKey, storeFieldKey, StringComparison.Ordinal));
            if (index < 0)
            {
                return;
            }

            _failures.RemoveAt(index);
            // Keeps TakeUnreported's drain index pointing at the same next failure.
            if (index < _reportedFailureCount)
            {
                _reportedFailureCount--;
            }
        }

        /// <summary>
        /// The failures no earlier call returned. An apply names each failure once this way, while
        /// <see cref="Failures"/> keeps listing all of them for a status read.
        /// </summary>
        internal IReadOnlyList<HotReloadWiredValueRestoreFailure> TakeUnreported()
        {
            List<HotReloadWiredValueRestoreFailure> unreported = _failures.GetRange(
                _reportedFailureCount, _failures.Count - _reportedFailureCount);
            _reportedFailureCount = _failures.Count;
            return unreported;
        }

        internal void Reset()
        {
            RestoredCount = 0;
            _failures.Clear();
            _reportedFailureCount = 0;
        }
    }
}
