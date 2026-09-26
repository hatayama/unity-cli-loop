using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The removed-member names the last run that reported any showed for each file, so a later
    /// run can tell whether it is repeating the same report.
    /// </summary>
    /// <remarks>
    /// Why the last displayed set is kept per file rather than derived from the worker output:
    /// the removed members of a run are recomputed from scratch every time, so nothing in a
    /// single run can tell a set the previous run already reported from one it never did.
    /// </remarks>
    internal sealed class HotReloadDisplayedRemovedMemberLedger
    {
        private readonly Dictionary<string, HashSet<string>> _displayedByPath =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        /// <summary>
        /// Answers whether the last run that reported removed members for one file reported
        /// exactly this set, without changing the record.
        /// </summary>
        internal bool IsSameAsLastDisplayed(
            string projectRelativePath,
            IReadOnlyList<string> displayedNames)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(displayedNames != null, "displayedNames must not be null.");

            return displayedNames.Count > 0
                && _displayedByPath.TryGetValue(projectRelativePath, out HashSet<string> lastDisplayed)
                && lastDisplayed.SetEquals(displayedNames);
        }

        /// <summary>
        /// Takes the removed-member names a run is about to report for one file and answers
        /// whether the last run that reported any for it reported exactly the same set. An empty
        /// set drops the record, so a run that reports nothing is not a gap inside a continuation.
        /// </summary>
        internal bool Record(
            string projectRelativePath,
            IReadOnlyList<string> displayedNames)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(displayedNames != null, "displayedNames must not be null.");

            if (displayedNames.Count == 0)
            {
                _displayedByPath.Remove(projectRelativePath);
                return false;
            }

            HashSet<string> displayed = new HashSet<string>(displayedNames, StringComparer.Ordinal);
            bool isSameAsLastDisplayed =
                _displayedByPath.TryGetValue(projectRelativePath, out HashSet<string> lastDisplayed)
                && lastDisplayed.SetEquals(displayed);
            _displayedByPath[projectRelativePath] = displayed;
            return isSameAsLastDisplayed;
        }

        internal void Clear()
        {
            _displayedByPath.Clear();
        }
    }
}
