using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Holds the removed-member names each file's warning listed during one run, and writes them
    /// to the patcher's record once the run ends.
    /// </summary>
    /// <remarks>
    /// Why the run holds them instead of writing the record where the warning is chosen: an input
    /// that lists one file twice runs its second copy in a later group of the same run. That copy
    /// has to be compared against the record the run started with, not the one its first copy
    /// would have just written, or it reports "Continuing from an earlier run" within one run.
    /// </remarks>
    internal sealed class HotReloadRunDisplayedRemovedMembers
    {
        // Why keyed like the patcher's record and overwritten per stage: the set staged last for a
        // path is what that file reported last in the run, which is its state when the run ends.
        private readonly Dictionary<string, IReadOnlyList<string>> _displayedByPath =
            new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);

        internal void Stage(string projectRelativePath, IReadOnlyList<string> displayedNames)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(displayedNames != null, "displayedNames must not be null.");

            _displayedByPath[projectRelativePath] = displayedNames;
        }

        /// <summary>
        /// Writes every staged set to the patcher's record. Call once after every group of the
        /// run finished, so a run that fails or is cancelled before then writes nothing.
        /// </summary>
        internal void ApplyTo(HotReloadPatcher patcher)
        {
            Debug.Assert(patcher != null, "patcher must not be null.");

            // Why an empty set is written too: the set is the file's, not this run's, so a run
            // that reports none has to end the continuation.
            foreach (KeyValuePair<string, IReadOnlyList<string>> pair in _displayedByPath)
            {
                // Why the comparison it returns is dropped: every entry of the run was already
                // compared against the record as it stood when the run started.
                patcher.RecordDisplayedRemovedMembers(pair.Key, pair.Value);
            }
        }
    }
}
