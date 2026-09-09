using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// The warnings and outcome rows a group run reports about what the worker and the
    /// signature-change gate decided, before and after the entries to patch are final.
    /// </summary>
    /// <remarks>
    /// Why separate from the pipeline: none of them reads the group's collaborators, they only
    /// route worker and gate rows into the per-file sinks.
    /// </remarks>
    internal static class HotReloadGroupNotices
    {
        // Returns false when a replacement lost its covering caller and the group must fail.
        internal static bool AppendSignatureChangeCoverageNotices(
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult,
            HotReloadGroupCompileResult compile)
        {
            // Why after entriesToPatch is final and before Harmony: isolation or a gate
            // retry can drop a covering caller without dropping the replacement. A third
            // worker run is not allowed (max two); fail the group instead of applying.
            List<string> lostReplacementKeys = HotReloadSignatureChangeCoverage.FindSignatureChangeCoverageLosses(
                context.AssemblyName,
                compile.EntriesToPatch,
                gateResult.Hits,
                gateResult.DeletedCallerExemptions);
            if (lostReplacementKeys.Count > 0)
            {
                HotReloadGroupOutcomeRouter.AppendGroupFailure(
                    context.Files,
                    "(signature-change-gate)",
                    string.Format(
                        HotReloadConstants.SignatureChangeCoverageLostFailureFormat,
                        string.Join(", ", lostReplacementKeys)));
                return false;
            }

            foreach (HotReloadGroupFile file in context.Files)
            {
                // Why the group's entries with this file's labels: a caller in one file can cover
                // a replacement in another, and the snapshot labels decide which file's response
                // the warning belongs to.
                HotReloadSignatureChangeCoverage.AppendSignatureChangeCallersRepatchedWarnings(
                    file.Sinks.Warnings,
                    context.AssemblyName,
                    compile.EntriesToPatch,
                    gateResult.Hits,
                    file.SnapshotLabels);
            }

            return true;
        }

        internal static void AppendRemovedMemberNotices(
            HotReloadPatcher patcher,
            HotReloadApplyContext context,
            HotReloadSignatureChangeGate.SignatureChangeGateResult gateResult)
        {
            foreach (HotReloadGroupFile file in context.Files)
            {
                // Why after the gate: a gated replacement is not applied, so listing it under
                // "Removed members stay present... edited bodies no longer call them" is false.
                string removedMembersWarning = HotReloadRemovedMembersWarning.FormatRemovedMembersWarning(
                    file.FileOutput.removedMembers,
                    file.FileOutput.removedMethodSignatures,
                    gateResult.GatedReplacementMethodKeys);
                if (removedMembersWarning != null)
                {
                    file.Sinks.Warnings.Add(removedMembersWarning);
                }

                HotReloadStalePatchOutcomes.Append(
                    patcher,
                    file.Sinks.Outcomes,
                    context.WorkerOutput,
                    file.FileOutput.removedMethodSignatures,
                    gateResult.GatedReplacementMethodKeys,
                    file.ProjectRelativePath,
                    file.AssemblyResolvePath);
            }
        }

        // internal so a test can observe the per-file notices, including the SkipApply decision,
        // without going through the apply stage the group tests replace wholesale.
        internal static void AppendPerFileWorkerNotices(
            IReadOnlyList<HotReloadGroupFile> files,
            HotReloadWorkerRowsByFile rows)
        {
            foreach (HotReloadGroupFile file in files)
            {
                IReadOnlyList<TransformWorkerSkippedDto> fileSkipped = rows.SkippedFor(file.ProjectRelativePath);
                int patchCandidateRowCount = rows.EntriesFor(file.ProjectRelativePath).Count
                    + fileSkipped.Count
                    + rows.UnchangedFor(file.ProjectRelativePath).Count;
                file.FileOutput = rows.FileOutputFor(file.ProjectRelativePath);
                // Why SkipApply and not an empty entry list: only SkipApply leaves the file
                // unapplied while keeping its generations, so the previous reload's patches stay
                // active. The NoEntriesToApply path clears the generation instead.
                file.SkipApply = file.FileOutput.parseErrors != null && file.FileOutput.parseErrors.Length > 0;
                file.UnchangedMethodCount = rows.UnchangedFor(file.ProjectRelativePath).Count;
                HotReloadWorkerNoticeAppender.AppendWorkerNotices(
                    file.FileOutput,
                    fileSkipped,
                    patchCandidateRowCount,
                    file.SnapshotSource,
                    file.ProjectRelativePath,
                    file.AssemblyName,
                    file.AssemblyResolvePath,
                    file.Sinks.Outcomes,
                    file.Sinks.Warnings);
            }
        }
    }
}
