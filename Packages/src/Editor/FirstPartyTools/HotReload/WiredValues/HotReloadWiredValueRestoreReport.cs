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
