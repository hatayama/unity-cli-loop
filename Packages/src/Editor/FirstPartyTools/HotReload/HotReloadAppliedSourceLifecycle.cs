using System;
using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Applied-source hash short-circuit, staging, and unexpected patch-deactivation warnings.
    /// </summary>
    internal static class HotReloadAppliedSourceLifecycle
    {
        // What: decide whether an unchanged source should short-circuit, re-apply with a
        // non-baseline warning, or fall through as a normal changed/unknown source.
        // Why Clear on the miss and non-baseline paths: a later Failed run or revert can leave
        // the hash pointing at a different live patch set; the next reload must not inherit
        // that stale hash. Non-baseline matches still Clear; Stage/Record writes the same
        // hash+flag back so the next identical reload warns again.
        internal static HotReloadUnchangedSourceDecision TryShortCircuitUnchangedAppliedSource(
            string workerSourcePath,
            string projectRelativePath,
            string assemblyResolvePath,
            List<HotReloadMethodOutcome> outcomes)
        {
            Debug.Assert(!string.IsNullOrEmpty(workerSourcePath), "workerSourcePath must not be empty.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(outcomes != null, "outcomes must not be null.");

            // Why Exists (not ReadAllBytes first): a missing file after a successful apply
            // used to surface as a file-level Failed from the worker. Reading unconditionally
            // would throw and abort the whole RunAsync. Why not Clear: this path does not
            // mutate patches, so the ledger still describes the live patch set.
            string fullWorkerSourcePath = Path.GetFullPath(workerSourcePath);
            if (!File.Exists(fullWorkerSourcePath))
            {
                return HotReloadUnchangedSourceDecision.NotUnchanged;
            }

            byte[] probeBytes = File.ReadAllBytes(fullWorkerSourcePath);
            string probeHash = HotReloadAppliedSourceLedger.ComputeContentHash(probeBytes);
            HashSet<string> activeLabels = CollectActiveLabelsForFile(projectRelativePath);
            (string Hash, bool IsFullyApplied)? recorded = HotReloadAppliedSourceLedger.TryGet(projectRelativePath);
            if (recorded == null
                || !string.Equals(probeHash, recorded.Value.Hash, StringComparison.Ordinal)
                || (recorded.Value.IsFullyApplied && activeLabels.Count == 0))
            {
                HotReloadAppliedSourceLedger.Clear(projectRelativePath);
                return HotReloadUnchangedSourceDecision.NotUnchanged;
            }

            if (recorded.Value.IsFullyApplied)
            {
                List<string> sortedLabels = new List<string>(activeLabels);
                sortedLabels.Sort(StringComparer.Ordinal);
                for (int index = 0; index < sortedLabels.Count; index++)
                {
                    string label = sortedLabels[index];
                    string reason = HotReloadAddedMemberRegistry.IsActiveMember(
                        projectRelativePath,
                        label)
                        ? HotReloadConstants.AlreadyActiveAddedMemberReason
                        : HotReloadConstants.AlreadyActiveReason;
                    outcomes.Add(
                        HotReloadMethodOutcome.AlreadyActive(label, assemblyResolvePath, reason));
                }

                return HotReloadUnchangedSourceDecision.ShortCircuited;
            }

            HotReloadAppliedSourceLedger.Clear(projectRelativePath);
            return HotReloadUnchangedSourceDecision.ReapplyNonBaseline;
        }

        // Why worker hash (not the orchestrator probe): the worker re-reads the file in another
        // process, so the bytes it compiled can differ from the probe if the file changed mid-run.
        // Why last occurrence wins: duplicate paths in one run apply twice; only the last
        // qualifying hash is recorded so the next run short-circuits against what actually landed.
        internal static void StageAppliedSourceHash(
            Dictionary<string, (string Hash, bool IsFullyApplied)> appliedSourceHashByPath,
            string projectRelativePath,
            string sourceContentSha256,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            Debug.Assert(appliedSourceHashByPath != null, "appliedSourceHashByPath must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            Debug.Assert(outcomes != null, "outcomes must not be null.");

            (string Hash, bool IsFullyApplied)? record = DecideAppliedSourceRecord(
                sourceContentSha256,
                outcomes);
            if (record == null)
            {
                appliedSourceHashByPath.Remove(projectRelativePath);
                return;
            }

            appliedSourceHashByPath[projectRelativePath] = record.Value;
        }

        // Why not record "everything that is not fully applied": deleting an added method and
        // converging to compiled IL yields empty outcomes on the empty-entries path. Recording
        // that as non-baseline would make the next identical reload claim a prior Skipped/Failed
        // that never happened.
        private static (string Hash, bool IsFullyApplied)? DecideAppliedSourceRecord(
            string sourceContentSha256,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            if (string.IsNullOrEmpty(sourceContentSha256) || outcomes.Count == 0)
            {
                return null;
            }

            bool hasSkippedOrFailed = false;
            bool allPatchedOrAdded = true;
            for (int index = 0; index < outcomes.Count; index++)
            {
                HotReloadMethodOutcomeKind kind = outcomes[index].Kind;
                if (kind == HotReloadMethodOutcomeKind.Patched
                    || kind == HotReloadMethodOutcomeKind.Added)
                {
                    continue;
                }

                allPatchedOrAdded = false;
                if (kind == HotReloadMethodOutcomeKind.Skipped
                    || kind == HotReloadMethodOutcomeKind.Failed)
                {
                    hasSkippedOrFailed = true;
                }
            }

            if (allPatchedOrAdded)
            {
                return (sourceContentSha256, true);
            }

            if (hasSkippedOrFailed)
            {
                return (sourceContentSha256, false);
            }

            return null;
        }

        internal static HashSet<string> CollectActiveLabelsForFile(string projectRelativePath)
        {
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");
            HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
            IReadOnlyList<string> addedKeys =
                HotReloadAddedMemberRegistry.ListActiveMethodKeys(projectRelativePath);
            for (int index = 0; index < addedKeys.Count; index++)
            {
                labels.Add(addedKeys[index]);
            }

            IReadOnlyList<string> patchedKeys = HotReloadPatcher.ListActiveMethodKeys(projectRelativePath);
            for (int index = 0; index < patchedKeys.Count; index++)
            {
                labels.Add(patchedKeys[index]);
            }

            return labels;
        }

        // Why first-pass added entries: a return-type replacement is both an added entry and
        // a removed signature with the same label, so subtracting removals would swallow the
        // warning. Convergence is quiet because dropping the declaration also drops the entry.
        private static HashSet<string> CollectAddedEntryLabels(TransformWorkerOutputDto workerOutput)
        {
            HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
            if (workerOutput == null || workerOutput.entries == null)
            {
                return labels;
            }

            foreach (TransformWorkerEntryDto entry in workerOutput.entries)
            {
                if (entry == null || entry.patchKind != HotReloadConstants.PatchKindAddedMethod)
                {
                    continue;
                }

                labels.Add(
                    HotReloadPatcher.FormatMethodKeyParts(
                        entry.typeMetadataName,
                        entry.methodName,
                        entry.parameterTypeFullNames ?? Array.Empty<string>(),
                        entry.genericArity));
            }

            return labels;
        }

        // Why union Skipped labels: a still-declared added method can leave the first-pass
        // entries when the worker skips it (virtual, generic, interface). Why not Failed:
        // a Failed added method is always a first-pass added entry.
        private static HashSet<string> CollectStillDeclaredAddedLabels(
            TransformWorkerOutputDto workerOutput,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            HashSet<string> labels = CollectAddedEntryLabels(workerOutput);
            if (outcomes == null)
            {
                return labels;
            }

            foreach (HotReloadMethodOutcome outcome in outcomes)
            {
                if (outcome == null
                    || outcome.Kind != HotReloadMethodOutcomeKind.Skipped
                    || string.IsNullOrEmpty(outcome.Method))
                {
                    continue;
                }

                labels.Add(outcome.Method);
            }

            return labels;
        }

        private static bool IsUnexpectedDeactivation(
            string label,
            HashSet<string> currentLabels,
            HashSet<string> stillDeclaredAddedLabels)
        {
            return !currentLabels.Contains(label) && stillDeclaredAddedLabels.Contains(label);
        }

        internal static void AppendDeactivatedPatchesWarning(
            List<string> warnings,
            HashSet<string> snapshotLabels,
            HashSet<string> snapshotAddedLabels,
            string projectRelativePath,
            TransformWorkerOutputDto workerOutput,
            IReadOnlyList<HotReloadMethodOutcome> outcomes)
        {
            Debug.Assert(warnings != null, "warnings must not be null.");
            Debug.Assert(snapshotLabels != null, "snapshotLabels must not be null.");
            Debug.Assert(snapshotAddedLabels != null, "snapshotAddedLabels must not be null.");
            Debug.Assert(!string.IsNullOrEmpty(projectRelativePath), "projectRelativePath must not be empty.");

            HashSet<string> currentLabels = CollectActiveLabelsForFile(projectRelativePath);
            HashSet<string> stillDeclaredAdded = CollectStillDeclaredAddedLabels(workerOutput, outcomes);
            List<string> deactivatedAdded = new List<string>();
            List<string> deactivatedPatches = new List<string>();
            foreach (string label in snapshotLabels)
            {
                if (!IsUnexpectedDeactivation(label, currentLabels, stillDeclaredAdded))
                {
                    continue;
                }

                if (snapshotAddedLabels.Contains(label))
                {
                    deactivatedAdded.Add(label);
                }
                else
                {
                    deactivatedPatches.Add(label);
                }
            }

            AppendDeactivatedWarningLine(
                warnings,
                deactivatedAdded,
                HotReloadConstants.DeactivatedAddedMembersWarningFormat);
            AppendDeactivatedWarningLine(
                warnings,
                deactivatedPatches,
                HotReloadConstants.DeactivatedPatchesWarningFormat);
        }

        private static void AppendDeactivatedWarningLine(
            List<string> warnings,
            List<string> labels,
            string format)
        {
            if (labels.Count == 0)
            {
                return;
            }

            labels.Sort(string.CompareOrdinal);
            warnings.Add(string.Format(format, string.Join(", ", labels)));
        }
    }
}
