using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

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
        private readonly Dictionary<string, HotReloadAppliedSourceRecord> _appliedSourceByPath =
            new Dictionary<string, HotReloadAppliedSourceRecord>(StringComparer.Ordinal);

        // Why it is keyed by the platform's path comparer rather than Ordinal: a file absent from
        // the compiled source list is looked up again from a later run's spelling of the path,
        // which on Windows can differ in case only.
        private readonly Dictionary<string, HotReloadNewSourceMembershipEvidence> _newSourceMembershipEvidenceByPath =
            new Dictionary<string, HotReloadNewSourceMembershipEvidence>(
                HotReloadSourcePathNormalizer.ProjectRelativePathComparer());

        // Why the flag: a non-baseline entry (Skipped or Failed in the last run) must not
        // short-circuit; it exists only so an identical reload can explain why it re-applies.
        // Why the path and the rows: the pause-point tool asks whether the file on disk is still
        // what that reload read, and which rows it left unapplied.
        internal void RecordAppliedSource(
            string projectRelativePath,
            string sourceContentSha256,
            bool isFullyApplied,
            string sourcePath,
            IReadOnlyList<HotReloadUnappliedRow> unappliedRows)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            _appliedSourceByPath[projectRelativePath] = new HotReloadAppliedSourceRecord(
                sourceContentSha256,
                isFullyApplied,
                sourcePath,
                unappliedRows);
        }

        /// <summary>The files whose last reload left Skipped or Failed rows.</summary>
        internal IReadOnlyList<string> ListNotFullyAppliedSourcePaths()
        {
            List<string> paths = new List<string>();
            foreach (KeyValuePair<string, HotReloadAppliedSourceRecord> pair in _appliedSourceByPath)
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

            if (!_appliedSourceByPath.TryGetValue(projectRelativePath, out HotReloadAppliedSourceRecord record))
            {
                return null;
            }

            return (record.Hash, record.IsFullyApplied);
        }

        /// <summary>
        /// The record a caller outside the apply pipeline means by this path, which may be absolute
        /// or spelled with the other separator. Null when no record matches, or when more than one
        /// does.
        /// </summary>
        internal HotReloadAppliedSourceRecord FindRecordForRequestedPath(string requestedPath)
        {
            if (string.IsNullOrEmpty(requestedPath))
            {
                return null;
            }

            string normalizedRequest = HotReloadSourcePathNormalizer.ToForwardSlashes(requestedPath);
            StringComparison comparison = Path.DirectorySeparatorChar == '\\'
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

            // Why exact-first: the same rule as the shim lookup, so a path that names one file
            // exactly is never lost to a suffix match on another.
            foreach (KeyValuePair<string, HotReloadAppliedSourceRecord> pair in _appliedSourceByPath)
            {
                if (string.Equals(normalizedRequest, HotReloadSourcePathNormalizer.ToForwardSlashes(pair.Key), comparison))
                {
                    return pair.Value;
                }
            }

            HotReloadAppliedSourceRecord suffixMatch = null;
            int suffixMatchCount = 0;
            foreach (KeyValuePair<string, HotReloadAppliedSourceRecord> pair in _appliedSourceByPath)
            {
                if (!HotReloadSourcePathNormalizer.PathsReferToSameFile(requestedPath, pair.Key))
                {
                    continue;
                }

                suffixMatchCount++;
                suffixMatch = pair.Value;
                if (suffixMatchCount > 1)
                {
                    // Why null on ambiguity: answering from the wrong file's reload is worse than
                    // answering as if no reload read the file.
                    return null;
                }
            }

            return suffixMatch;
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

    /// <summary>
    /// What the latest reload that read a file recorded about it.
    /// </summary>
    internal sealed class HotReloadAppliedSourceRecord
    {
        internal HotReloadAppliedSourceRecord(
            string hash,
            bool isFullyApplied,
            string sourcePath,
            IReadOnlyList<HotReloadUnappliedRow> unappliedRows)
        {
            Debug.Assert(!string.IsNullOrEmpty(hash), "hash must not be empty.");

            // Why these two stop in every build: a record with no path would let the pause-point
            // port report the file as unchanged, and one with no rows would claim a clean reload.
            if (string.IsNullOrEmpty(sourcePath))
            {
                throw new ArgumentException("An applied-source record needs the path the reload read.", nameof(sourcePath));
            }

            if (unappliedRows == null)
            {
                throw new ArgumentNullException(nameof(unappliedRows));
            }

            Hash = hash;
            IsFullyApplied = isFullyApplied;
            SourcePath = sourcePath;
            UnappliedRows = unappliedRows;
        }

        /// <summary>The hash of the bytes that reload read.</summary>
        internal string Hash { get; }

        /// <summary>Whether that reload applied every change without a Skipped or Failed row.</summary>
        internal bool IsFullyApplied { get; }

        /// <summary>
        /// The full path the transform worker read, which differs from the file itself when a
        /// reload was run from an edited copy.
        /// </summary>
        internal string SourcePath { get; }

        /// <summary>The Skipped and Failed rows of that reload, in the order it reported them.</summary>
        internal IReadOnlyList<HotReloadUnappliedRow> UnappliedRows { get; }
    }
}
