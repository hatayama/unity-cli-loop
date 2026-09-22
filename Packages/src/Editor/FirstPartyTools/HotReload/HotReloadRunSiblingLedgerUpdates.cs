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

        internal HotReloadRunSiblingLedgerUpdates(HotReloadDomain domain)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            _pathsActiveAtStart = new HashSet<string>(domain.ListActiveFilePaths(), _comparer);
            _pathsActiveAtStart.UnionWith(domain.ListPathsWithActiveAddedMembers());
            // Why the declaring files of introduced types count as active: removing the last
            // introduced type from such a file leaves no row, yet the file had changes of its own,
            // and a Play-entry reload drops the types while the companion ledger comes back.
            IReadOnlyList<HotReloadIntroducedTypeDescriptor> introducedTypes = domain.IntroducedTypes.DescribeActive();
            for (int index = 0; index < introducedTypes.Count; index++)
            {
                if (!string.IsNullOrEmpty(introducedTypes[index].OwnerProjectRelativePath))
                {
                    _pathsActiveAtStart.Add(introducedTypes[index].OwnerProjectRelativePath);
                }
            }

            _reasonByPath = new Dictionary<string, HotReloadSiblingInclusionReason>(_comparer);
        }

        internal void NoteInclusion(string projectRelativePath, HotReloadSiblingInclusionReason reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            _reasonByPath[projectRelativePath] = reason;
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

        // Why rows of any kind disqualify a file: a row means it had something of its own to apply
        // or refuse, and the applied-source record then speaks for it instead.
        private static bool IsCompanionResult(HotReloadFileProcessResult fileResult)
        {
            return fileResult.Outcomes.Count == 0
                && fileResult.IntroducedTypes.Count == 0
                && fileResult.AddedFieldNames.Length == 0
                && fileResult.AddedConstNames.Length == 0
                && fileResult.RevertedUnchangedCount == 0
                && !string.IsNullOrEmpty(fileResult.SourceContentSha256);
        }
    }
}
