using System;
using System.Collections.Generic;
using System.Globalization;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Merges per-file apply results into one run: outcome and warning lists, distinct pause-point
    /// and inline-risk ids, added-member names, totals, and the applied-source hashes to record.
    /// The orchestrator feeds it one <see cref="HotReloadFileProcessResult"/> per processed file
    /// and asks it for the final <see cref="HotReloadOrchestratorResult"/> once every file is done.
    /// </summary>
    internal sealed class HotReloadRunAccumulator
    {
        private readonly List<HotReloadMethodOutcome> _outcomes = new List<HotReloadMethodOutcome>();
        private readonly List<string> _warnings = new List<string>();
        private readonly List<string> _suppressedPausePointIds = new List<string>();
        private readonly List<string> _retargetedPausePointIds = new List<string>();
        private readonly List<string> _inlineRiskMethodLabels = new List<string>();
        private readonly List<string> _addedFields = new List<string>();
        private readonly List<string> _addedConsts = new List<string>();
        private readonly List<string> _siblingDerivedWarnings = new List<string>();
        private readonly List<string> _reappliedSiblingPaths = new List<string>();
        // Why appended without deduplication: one row per declaration is what the report means,
        // and two files declaring the same type is a mistake the run has to report against both.
        private readonly List<HotReloadIntroducedTypeOutcome> _introducedTypes =
            new List<HotReloadIntroducedTypeOutcome>();
        private readonly List<HotReloadOneShotCallerNoteEnricher.Candidate> _oneShotCallerNoteCandidates =
            new List<HotReloadOneShotCallerNoteEnricher.Candidate>();
        // Why staged (not recorded per file): duplicate paths in one run must still apply
        // twice; recording mid-run would short-circuit the second copy.
        private readonly Dictionary<string, (string Hash, bool IsFullyApplied)> _appliedSourceHashByPath =
            new Dictionary<string, (string Hash, bool IsFullyApplied)>(StringComparer.Ordinal);
        // Why captured at construction: the 0.5s Auto Refresh reconcile can arm the hold while the
        // run is still awaited, so the sync at the end of the run cannot tell a run that armed the
        // hold from one that merely found it armed. What the caller promised is "the first apply
        // that arms the hold", which only the state before the run answers.
        private readonly bool _autoRefreshHeldAtStart;
        private readonly HotReloadDomain _domain;
        private readonly HotReloadPatcher _patcher;
        private int _patchedTotal;
        private int _unchangedTotal;
        private int _revertedUnchangedTotal;

        /// <param name="autoRefreshHeldAtStart">
        /// Whether the Auto Refresh hold was already armed when the run started. Read on the Unity
        /// main thread, because the flag lives in SessionState.
        /// </param>
        public HotReloadRunAccumulator(
            HotReloadDomain domain,
            HotReloadPatcher patcher,
            bool autoRefreshHeldAtStart)
        {
            Debug.Assert(domain != null, "domain must not be null.");
            Debug.Assert(patcher != null, "patcher must not be null.");
            _domain = domain;
            _patcher = patcher;
            _autoRefreshHeldAtStart = autoRefreshHeldAtStart;
        }

        /// <summary>Warning sink shared with the per-file stage for sibling-derived notices.</summary>
        public List<string> SiblingDerivedWarnings => _siblingDerivedWarnings;

        /// <summary>Candidate sink shared with the per-file stage for one-shot lifecycle notes.</summary>
        public List<HotReloadOneShotCallerNoteEnricher.Candidate> OneShotCallerNoteCandidates =>
            _oneShotCallerNoteCandidates;

        /// <summary>Merges one processed file into the run.</summary>
        public void Add(string projectRelativePath, HotReloadFileProcessResult fileResult)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be null or empty.");
            Debug.Assert(fileResult != null, "fileResult must not be null.");

            _outcomes.AddRange(fileResult.Outcomes);
            _warnings.AddRange(fileResult.Warnings);
            HotReloadOutcomeAggregation.AppendDistinct(_suppressedPausePointIds, fileResult.SuppressedPausePointIds);
            HotReloadOutcomeAggregation.AppendDistinct(_retargetedPausePointIds, fileResult.RetargetedPausePointIds);
            HotReloadOutcomeAggregation.AppendDistinct(_inlineRiskMethodLabels, fileResult.InlineRiskMethodLabels);
            _patchedTotal += fileResult.PatchedCount;
            _unchangedTotal += fileResult.UnchangedMethodCount;
            _revertedUnchangedTotal += fileResult.RevertedUnchangedCount;
            _addedFields.AddRange(fileResult.AddedFieldNames);
            _addedConsts.AddRange(fileResult.AddedConstNames);
            _introducedTypes.AddRange(fileResult.IntroducedTypes);
            HotReloadAppliedSourceLifecycle.StageAppliedSourceHash(
                _appliedSourceHashByPath,
                projectRelativePath,
                fileResult.SourceContentSha256,
                fileResult.Outcomes);
        }

        /// <summary>
        /// Merges one auto-reapplied sibling file and records its project-relative path.
        /// </summary>
        public void AddReappliedSibling(string projectRelativePath, HotReloadFileProcessResult fileResult)
        {
            // Overloads are forbidden, so this distinct name both Adds and records extraResults
            // paths for continuing line-shift aggregation.
            Add(projectRelativePath, fileResult);
            _reappliedSiblingPaths.Add(projectRelativePath);
        }

        /// <summary>Writes the staged applied-source hashes to the ledger. Call once after every file was added.</summary>
        public void RecordAppliedSourceHashes()
        {
            foreach (KeyValuePair<string, (string Hash, bool IsFullyApplied)> pair in _appliedSourceHashByPath)
            {
                _domain.RecordAppliedSource(
                    pair.Key,
                    pair.Value.Hash,
                    pair.Value.IsFullyApplied);
            }
        }

        /// <summary>
        /// Attaches one-shot lifecycle notes to the merged outcomes. Requires the Unity main thread
        /// because the call-site scan reads compiled assemblies through Editor APIs.
        /// </summary>
        public void ApplyOneShotCallerNotes(string projectRoot)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRoot), "projectRoot must not be null or empty.");
            HotReloadOneShotCallerNoteEnricher.ApplyNotes(
                projectRoot,
                _outcomes,
                _oneShotCallerNoteCandidates,
                (ignoredAssemblyName, identities) => HotReloadCallSiteScanner.FindCallSites(projectRoot, identities));
        }

        /// <summary>
        /// Appends the run-level warnings, logs the summary, syncs the Auto Refresh hold, and
        /// builds the final result. Requires the Unity main thread for the Auto Refresh sync.
        /// </summary>
        public HotReloadOrchestratorResult BuildResult(string correlationId)
        {
            AppendInlineRiskWarning();
            AppendAddedFieldsLifetimeWarning();
            LogSummary(correlationId);
            HotReloadOutcomeAggregation.AppendSiblingDerivedWarnings(_warnings, _siblingDerivedWarnings);
            HotReloadAutoRefreshHoldSyncResult autoRefreshHold =
                HotReloadAutoRefreshHold.SyncToActiveChanges();
            // Why not autoRefreshHold.NewlyArmed: that reports whether this one Sync call armed
            // the hold, which the periodic reconcile can win. The run armed it whenever it started
            // with Auto Refresh allowed and ended with it held.
            bool newlyArmed = autoRefreshHold.Held && !_autoRefreshHeldAtStart;
            return new HotReloadOrchestratorResult(
                methods: _outcomes,
                warnings: _warnings,
                patchedTotal: _patchedTotal,
                activePatchTotal: _patcher.ActiveChangeCount,
                suppressedPausePointIds: _suppressedPausePointIds,
                unchangedTotal: _unchangedTotal,
                retargetedPausePointIds: _retargetedPausePointIds,
                addedFields: _addedFields.ToArray(),
                addedConsts: _addedConsts.ToArray(),
                revertedUnchangedTotal: _revertedUnchangedTotal,
                autoRefreshHold: autoRefreshHold,
                reappliedSiblingPaths: _reappliedSiblingPaths.ToArray(),
                introducedTypes: _introducedTypes,
                autoRefreshHoldNewlyArmed: newlyArmed);
        }

        private void AppendInlineRiskWarning()
        {
            if (_inlineRiskMethodLabels.Count == 0)
            {
                return;
            }

            _warnings.Add(
                HotReloadOutcomeAggregation.FormatInlineRiskAggregatedWarning(
                    _inlineRiskMethodLabels.Count,
                    _patchedTotal,
                    _inlineRiskMethodLabels));
        }

        private void AppendAddedFieldsLifetimeWarning()
        {
            _addedFields.Sort(StringComparer.Ordinal);
            _addedConsts.Sort(StringComparer.Ordinal);
            if (_addedFields.Count == 0)
            {
                return;
            }

            // Why from this list: AddedFields and the lifetime warning must name the same
            // applied fields. Worker-side classified sets include unused and unavailable
            // declarations, and retry overwrites names without replacing first-pass warnings.
            _warnings.Add(
                string.Format(
                    CultureInfo.InvariantCulture,
                    HotReloadConstants.AddedFieldsLifetimeWarningFormat,
                    string.Join(", ", _addedFields)));
        }

        private void LogSummary(string correlationId)
        {
            HotReloadOutcomeTally tally = HotReloadOutcomeAggregation.CountMethodOutcomeKinds(_outcomes);
            HotReloadOrchestratorLog.LogHotReloadApplySummary(
                tally.PatchedCount,
                tally.FailedCount,
                tally.SkippedCount,
                tally.AlreadyActiveCount,
                tally.AddedCount,
                tally.StaleCount,
                !tally.HasFailure,
                correlationId);
        }
    }
}
