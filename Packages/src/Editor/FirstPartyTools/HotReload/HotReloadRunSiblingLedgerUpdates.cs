using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Collects, over one run, why each sibling came back and what the run changes in the domain's
    /// sibling records: the companion files it was given, the files that left the companion ledger
    /// by applying a change, and the retried files that lose their applied-source record.
    /// </summary>
    internal sealed class HotReloadRunSiblingLedgerUpdates
    {
        private readonly StringComparer _comparer = HotReloadSourcePathNormalizer.ProjectRelativePathComparer();
        // Why captured at construction: by the end of the run a file this run applied holds active
        // changes too, and only the state before the run tells a companion from a file that had
        // changes of its own and merely came back unchanged.
        private readonly HashSet<string> _pathsActiveAtStart;
        private readonly Dictionary<string, HotReloadSiblingInclusionReason> _reasonByPath;
        private readonly List<(string Path, string Hash)> _companionCandidates = new List<(string Path, string Hash)>();
        private readonly List<string> _appliedPaths = new List<string>();
        private readonly List<string> _unappliedRetryPaths = new List<string>();
        private readonly List<string> _changedCompanionPaths = new List<string>();
        private readonly Dictionary<string, string> _observedHashByPath;
        private bool _appliedToDomain;

        internal HotReloadRunSiblingLedgerUpdates(HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            _pathsActiveAtStart = new HotReloadDomainCarriedInLookup(domain).ListActivePaths();
            _observedHashByPath = new Dictionary<string, string>(_comparer);
            _reasonByPath = new Dictionary<string, HotReloadSiblingInclusionReason>(_comparer);
        }

        internal void NoteInclusion(string projectRelativePath, HotReloadSiblingInclusionReason reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            _reasonByPath[projectRelativePath] = reason;
        }

        /// <summary>Takes in a companion the run did not bring back because its source changed.</summary>
        internal void NoteChangedCompanion(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            _changedCompanionPaths.Add(projectRelativePath);
        }

        /// <summary>Why a sibling came back; a file the run was passed reads as active changes.</summary>
        internal HotReloadSiblingInclusionReason ReasonOf(string projectRelativePath)
        {
            return _reasonByPath.TryGetValue(projectRelativePath, out HotReloadSiblingInclusionReason reason)
                ? reason
                : HotReloadSiblingInclusionReason.ActiveChanges;
        }

        /// <summary>Takes in one processed file, whether passed or brought back as a sibling.</summary>
        internal void Observe(string projectRelativePath, HotReloadFileProcessResult fileResult)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(fileResult != null, "fileResult must not be null.");

            _observedHashByPath[projectRelativePath] = fileResult.SourceContentSha256 ?? string.Empty;
            if (HotReloadSiblingRebindWarningSelector.AppliedAnyChange(fileResult))
            {
                _appliedPaths.Add(projectRelativePath);
                return;
            }

            if (ReasonOf(projectRelativePath) == HotReloadSiblingInclusionReason.RetryAfterSkip)
            {
                _unappliedRetryPaths.Add(projectRelativePath);
                return;
            }

            if (IsCompanionResult(fileResult) && !_pathsActiveAtStart.Contains(projectRelativePath))
            {
                _companionCandidates.Add((projectRelativePath, fileResult.SourceContentSha256));
            }
        }

        /// <summary>
        /// Writes the run's sibling records. Call after the applied-source hashes were recorded,
        /// so a retried file's cleared record is not written back.
        /// </summary>
        internal void ApplyTo(HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null.");

            // Why a retry that applied nothing forgets the record: that record is what makes the
            // file a retry candidate, and one more try at the same bytes cannot end differently.
            // Why here and not in HotReloadAppliedSourceRecordDecision: that decision reads only the
            // file's result, and whether the file was a retry is known only from why the run
            // brought it back.
            for (int index = 0; index < _unappliedRetryPaths.Count; index++)
            {
                domain.ClearAppliedSource(_unappliedRetryPaths[index]);
            }

            for (int index = 0; index < _appliedPaths.Count; index++)
            {
                domain.CompanionSources.Remove(_appliedPaths[index]);
            }

            // Why a changed companion is forgotten: the ledger exists to bring back the same bytes,
            // an entry whose bytes changed can no longer do that, and keeping it repeats the
            // changed-source warning on every later reload even after the source is restored.
            for (int index = 0; index < _changedCompanionPaths.Count; index++)
            {
                domain.CompanionSources.Remove(_changedCompanionPaths[index]);
            }

            RecordCompanions(domain);
            // Why set only after every write: a write that throws leaves the records half written,
            // and DescribeAfterApply must not answer from them.
            _appliedToDomain = true;
        }

        private void RecordCompanions(HotReloadDomain domain)
        {
            // Why only a run that applied something records companions: an unchanged file matters
            // only as the context of a change, and a run of unchanged files alone bound nothing.
            if (_appliedPaths.Count == 0)
            {
                return;
            }

            for (int index = 0; index < _companionCandidates.Count; index++)
            {
                domain.CompanionSources.Record(_companionCandidates[index].Path, _companionCandidates[index].Hash);
            }
        }

        /// <summary>
        /// Where the file stands once this run's records are written: whether a later reload
        /// brings it back, and why.
        /// </summary>
        internal HotReloadCarriedInState DescribeAfterApply(IHotReloadCarriedInLookup lookup, string projectRelativePath)
        {
            if (lookup == null)
            {
                throw new ArgumentNullException(nameof(lookup));
            }

            // Why an exception and not an assert: read before the records are written, a file this
            // run is about to record would read as unrecorded, and the next step named from that
            // would send the reader after a fix that does not work.
            if (!_appliedToDomain)
            {
                throw new InvalidOperationException("The run's sibling records are read before they were written.");
            }

            if (!_observedHashByPath.TryGetValue(projectRelativePath, out string observedHash))
            {
                return HotReloadCarriedInState.NotInRun;
            }

            if (ContainsPath(_appliedPaths, projectRelativePath))
            {
                return HotReloadCarriedInState.AppliedInThisRun;
            }

            if (lookup.IsActive(projectRelativePath))
            {
                return HotReloadCarriedInState.ActiveFromEarlierRun;
            }

            return IsRecordedAt(lookup, projectRelativePath, observedHash)
                ? HotReloadCarriedInState.RecordedAtCurrentSource
                : HotReloadCarriedInState.NotRecorded;
        }

        // Why a record of other bytes does not count: the sibling planner brings a file back only
        // while its source hashes to a recorded hash. A companion found at other bytes is forgotten
        // by the run that finds it, so it comes back only after it is passed to a reload again.
        private static bool IsRecordedAt(IHotReloadCarriedInLookup lookup, string projectRelativePath, string hash)
        {
            if (string.IsNullOrEmpty(hash))
            {
                return false;
            }

            return string.Equals(lookup.TryGetCompanionHash(projectRelativePath), hash, StringComparison.Ordinal)
                || string.Equals(lookup.TryGetAppliedHash(projectRelativePath), hash, StringComparison.Ordinal);
        }

        private bool ContainsPath(List<string> paths, string projectRelativePath)
        {
            for (int index = 0; index < paths.Count; index++)
            {
                if (_comparer.Equals(paths[index], projectRelativePath))
                {
                    return true;
                }
            }

            return false;
        }

        // Why rows of any kind disqualify a file: a row means it had something of its own to apply
        // or refuse, and the applied-source record then speaks for it instead.
        // Why an added enum member disqualifies it too: it is not a row, yet the file holds an
        // edit hot reload cannot apply, and bringing it back beside a retained artifact splits the
        // enum's identity, so passing or leaving it out stays the reader's choice.
        private static bool IsCompanionResult(HotReloadFileProcessResult fileResult)
        {
            return fileResult.Outcomes.Count == 0
                && fileResult.IntroducedTypes.Count == 0
                && fileResult.AddedFieldNames.Length == 0
                && fileResult.AddedConstNames.Length == 0
                && fileResult.AddedEnumMemberNames.Length == 0
                && fileResult.RevertedUnchangedCount == 0
                && !string.IsNullOrEmpty(fileResult.SourceContentSha256);
        }
    }
}
