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

        /// <summary>
        /// Takes back one restored value, for a value the field's type rejected after the restore
        /// counted it.
        /// </summary>
        internal void RemoveRestored()
        {
            Debug.Assert(RestoredCount > 0, "RestoredCount must be positive before one is taken back.");
            RestoredCount--;
        }

        internal void AddFailure(string hostIdentity, string storeFieldKey, string reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(reason), "reason must not be empty.");
            _failures.Add(new HotReloadWiredValueRestoreFailure(hostIdentity, storeFieldKey, reason));
        }

        /// <summary>
        /// Lists a failure that was already handed out before its row was dropped, at the end of
        /// the handed-out rows, so <see cref="TakeUnreported"/> does not return it again.
        /// </summary>
        internal void AddFailureAlreadyReported(string hostIdentity, string storeFieldKey, string reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(reason), "reason must not be empty.");
            _failures.Insert(
                _reportedFailureCount,
                new HotReloadWiredValueRestoreFailure(hostIdentity, storeFieldKey, reason));
            _reportedFailureCount++;
        }

        /// <summary>
        /// Puts a new reason on the failure of this host and field where it stands, so neither the
        /// row order nor what <see cref="TakeUnreported"/> returns next changes. Does nothing when
        /// no such failure is listed.
        /// </summary>
        internal void ReplaceFailureReason(string hostIdentity, string storeFieldKey, string reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(reason), "reason must not be empty.");
            int index = FindFailureIndex(hostIdentity, storeFieldKey);
            if (index < 0)
            {
                return;
            }

            _failures[index] = new HotReloadWiredValueRestoreFailure(hostIdentity, storeFieldKey, reason);
        }

        /// <summary>
        /// Drops the failure of this host and field, for a value that came back after all.
        /// </summary>
        internal void RemoveFailure(string hostIdentity, string storeFieldKey)
        {
            int index = FindFailureIndex(hostIdentity, storeFieldKey);
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

        private int FindFailureIndex(string hostIdentity, string storeFieldKey)
        {
            return _failures.FindIndex(failure =>
                string.Equals(failure.HostIdentity, hostIdentity, StringComparison.Ordinal)
                && string.Equals(failure.StoreFieldKey, storeFieldKey, StringComparison.Ordinal));
        }

        internal void Reset()
        {
            RestoredCount = 0;
            _failures.Clear();
            _reportedFailureCount = 0;
        }
    }
}
