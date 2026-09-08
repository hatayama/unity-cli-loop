using System.Collections.Generic;
using System.IO;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Turns worker-side notices (missing baseline, parse errors, skipped rows, declaration drift)
    /// into per-file outcomes and warnings before any patch decision is made.
    /// </summary>
    internal static class HotReloadWorkerNoticeAppender
    {
        // Why after the worker: const-only / empty files have no patch candidates, so the
        // missing-baseline warning was pure noise (FB E). Emit only when the worker saw at
        // least one method or accessor row for this file.
        internal static void AppendWorkerNotices(
            TransformWorkerFileOutputDto fileOutput,
            IReadOnlyList<TransformWorkerSkippedDto> fileSkipped,
            int patchCandidateRowCountForFile,
            string snapshotSource,
            string projectRelativePath,
            string assemblyName,
            string assemblyResolvePath,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings)
        {
            Debug.Assert(fileOutput != null, "fileOutput must not be null.");
            Debug.Assert(fileSkipped != null, "fileSkipped must not be null.");
            Debug.Assert(patchCandidateRowCountForFile >= 0, "patchCandidateRowCountForFile must not be negative.");
            Debug.Assert(outcomes != null, "outcomes must not be null.");
            Debug.Assert(warnings != null, "warnings must not be null.");

            AppendBaselineNotices(
                fileOutput,
                patchCandidateRowCountForFile,
                snapshotSource,
                projectRelativePath,
                assemblyName,
                warnings);
            // Why a Failed outcome and not a warning: a parse error is the file failing, not a
            // remark about it. Under the all-or-nothing contract the Failed row is what leaves the
            // file unapplied and makes the response's Success false.
            if (fileOutput.parseErrors != null && fileOutput.parseErrors.Length > 0)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Failed(
                        "(file)",
                        string.Join("\n", fileOutput.parseErrors),
                        assemblyResolvePath));
            }

            AppendSkippedOutcomes(fileSkipped, assemblyResolvePath, outcomes);
            // Surfaced before the empty-entries early return so const drift still reaches
            // the response when every method in the file is skipped or unchanged.
            AppendAll(warnings, fileOutput.declarationDriftWarnings);
        }

        internal static void AppendRetrySiblingConstDriftWarnings(
            List<string> siblingDerivedWarnings,
            HotReloadShimIsolation.HotReloadShimIsolationResult isolation)
        {
            Debug.Assert(siblingDerivedWarnings != null, "siblingDerivedWarnings must not be null.");
            if (isolation == null)
            {
                return;
            }

            AppendAll(siblingDerivedWarnings, isolation.SiblingConstDriftWarnings);
        }

        private static void AppendBaselineNotices(
            TransformWorkerFileOutputDto fileOutput,
            int patchCandidateRowCountForFile,
            string snapshotSource,
            string projectRelativePath,
            string assemblyName,
            List<string> warnings)
        {
            if (snapshotSource == null && patchCandidateRowCountForFile >= 1)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.NoVerifiedSourceSnapshotWarningFormat,
                        Path.GetFileName(projectRelativePath),
                        assemblyName));
            }

            if (fileOutput.baselineDisabledByDuplicateKeys)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.BaselineDisabledByDuplicateKeysWarningFormat,
                        Path.GetFileName(projectRelativePath),
                        assemblyName));
            }
        }

        private static void AppendSkippedOutcomes(
            IReadOnlyList<TransformWorkerSkippedDto> skippedRows,
            string assemblyResolvePath,
            List<HotReloadMethodOutcome> outcomes)
        {
            foreach (TransformWorkerSkippedDto skipped in skippedRows)
            {
                outcomes.Add(
                    HotReloadMethodOutcome.Skipped(
                        skipped.method ?? "(unknown)",
                        skipped.reason ?? string.Empty,
                        assemblyResolvePath));
            }
        }

        private static void AppendAll(List<string> target, IReadOnlyList<string> additions)
        {
            if (additions == null)
            {
                return;
            }

            for (int index = 0; index < additions.Count; index++)
            {
                target.Add(additions[index]);
            }
        }
    }
}
