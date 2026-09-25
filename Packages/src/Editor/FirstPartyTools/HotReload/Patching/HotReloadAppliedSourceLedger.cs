using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The source hash each file was last applied from, and the new-source membership evidence
    /// that goes with that record.
    /// </summary>
    /// <remarks>
    /// Why these records live apart from a file generation: a file is probed for an unchanged
    /// re-apply before anything of it is applied, so the record has to outlive — and exist
    /// without — a generation of that file.
    /// </remarks>
    internal sealed class HotReloadAppliedSourceLedger
    {
        private readonly Dictionary<string, (string Hash, bool IsFullyApplied)> _appliedSourceByPath =
            new Dictionary<string, (string Hash, bool IsFullyApplied)>(StringComparer.Ordinal);

        // Why it is keyed by the platform's path comparer rather than Ordinal: a file absent from
        // the compiled source list is looked up again from a later run's spelling of the path,
        // which on Windows can differ in case only.
        private readonly Dictionary<string, HotReloadNewSourceMembershipEvidence> _newSourceMembershipEvidenceByPath =
            new Dictionary<string, HotReloadNewSourceMembershipEvidence>(
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());

        // Why the flag: a non-baseline entry (Skipped or Failed in the last run) must not
        // short-circuit; it exists only so an identical reload can explain why it re-applies.
        internal void RecordAppliedSource(
            string projectRelativePath,
            string sourceContentSha256,
            bool isFullyApplied)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(sourceContentSha256), "sourceContentSha256 must not be empty.");

            _appliedSourceByPath[projectRelativePath] = (sourceContentSha256, isFullyApplied);
        }

        /// <summary>The files whose last reload left Skipped or Failed rows.</summary>
        internal IReadOnlyList<string> ListNotFullyAppliedSourcePaths()
        {
            List<string> paths = new List<string>();
            foreach (KeyValuePair<string, (string Hash, bool IsFullyApplied)> pair in _appliedSourceByPath)
            {
                if (!pair.Value.IsFullyApplied)
                {
                    paths.Add(pair.Key);
                }
            }

            return paths;
        }

        internal (string Hash, bool IsFullyApplied)? TryGetAppliedSource(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            if (!_appliedSourceByPath.TryGetValue(
                    projectRelativePath,
                    out (string Hash, bool IsFullyApplied) entry))
            {
                return null;
            }

            return entry;
        }

        // Why the evidence is kept per file rather than recomputed: a file outside the last
        // compiled source list has no assembly Unity vouches for, so the only thing that can say
        // it still belongs to the assembly it was applied into is what the first reload verified.
        internal void RecordNewSourceMembershipEvidence(
            string projectRelativePath,
            HotReloadNewSourceMembershipEvidence evidence)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(evidence != null, "evidence must not be null.");

            _newSourceMembershipEvidenceByPath[projectRelativePath] = evidence;
        }

        internal HotReloadNewSourceMembershipEvidence TryGetNewSourceMembershipEvidence(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            if (!_newSourceMembershipEvidenceByPath.TryGetValue(
                    projectRelativePath,
                    out HotReloadNewSourceMembershipEvidence evidence))
            {
                return null;
            }

            return evidence;
        }

        // Why the evidence is dropped here rather than through its own method: it only means
        // anything alongside the applied record, so the two share one lifetime.
        internal void ClearAppliedSource(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            _appliedSourceByPath.Remove(projectRelativePath);
            _newSourceMembershipEvidenceByPath.Remove(projectRelativePath);
        }

        /// <summary>Forgets every record, as a revert-all does.</summary>
        internal void Clear()
        {
            _appliedSourceByPath.Clear();
            _newSourceMembershipEvidenceByPath.Clear();
        }
    }
}
