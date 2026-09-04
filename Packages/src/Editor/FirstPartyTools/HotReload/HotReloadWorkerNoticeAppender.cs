using System;
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
        // least one method or accessor row.
        internal static void AppendWorkerNotices(
            TransformWorkerOutputDto workerOutput,
            string snapshotSource,
            string projectRelativePath,
            string assemblyName,
            string assemblyResolvePath,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            List<string> siblingDerivedWarnings)
        {
            Debug.Assert(workerOutput != null, "workerOutput must not be null.");
            Debug.Assert(outcomes != null, "outcomes must not be null.");
            Debug.Assert(warnings != null, "warnings must not be null.");
            Debug.Assert(siblingDerivedWarnings != null, "siblingDerivedWarnings must not be null.");
            AssertEntryRowsCameFromTheEditedFile(workerOutput, projectRelativePath);

            AppendBaselineNotices(workerOutput, snapshotSource, projectRelativePath, assemblyName, warnings);
            AppendAll(warnings, workerOutput.parseErrors);
            AppendSkippedOutcomes(workerOutput.skipped, assemblyResolvePath, outcomes);
            // Surfaced before the empty-entries early return so const drift still reaches
            // the response when every method in the file is skipped or unchanged.
            AppendAll(warnings, workerOutput.declarationDriftWarnings);
            AppendAll(siblingDerivedWarnings, workerOutput.siblingConstDriftWarnings);
        }

        // States today's contract: one worker run covers one file, so every entry row it returns
        // must name that file. Grouping several files into one shim assembly will relax this.
        private static void AssertEntryRowsCameFromTheEditedFile(
            TransformWorkerOutputDto workerOutput,
            string projectRelativePath)
        {
            if (workerOutput.entries == null)
            {
                return;
            }

            foreach (TransformWorkerEntryDto entry in workerOutput.entries)
            {
                Debug.Assert(
                    string.Equals(entry.sourceProjectRelativePath, projectRelativePath, StringComparison.Ordinal),
                    "Worker entry row reports a different source file than the one this run edited.");
            }
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
            TransformWorkerOutputDto workerOutput,
            string snapshotSource,
            string projectRelativePath,
            string assemblyName,
            List<string> warnings)
        {
            if (snapshotSource == null && CountPatchCandidateRows(workerOutput) >= 1)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.NoVerifiedSourceSnapshotWarningFormat,
                        Path.GetFileName(projectRelativePath),
                        assemblyName));
            }

            if (workerOutput.baselineDisabledByDuplicateKeys)
            {
                warnings.Add(
                    string.Format(
                        HotReloadConstants.BaselineDisabledByDuplicateKeysWarningFormat,
                        Path.GetFileName(projectRelativePath),
                        assemblyName));
            }
        }

        private static void AppendSkippedOutcomes(
            TransformWorkerSkippedDto[] skippedRows,
            string assemblyResolvePath,
            List<HotReloadMethodOutcome> outcomes)
        {
            if (skippedRows == null)
            {
                return;
            }

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

        private static int CountPatchCandidateRows(TransformWorkerOutputDto workerOutput)
        {
            int entryCount = workerOutput.entries != null ? workerOutput.entries.Length : 0;
            int skippedCount = workerOutput.skipped != null ? workerOutput.skipped.Length : 0;
            int unchangedCount =
                workerOutput.unchangedMethods != null ? workerOutput.unchangedMethods.Length : 0;
            return entryCount + skippedCount + unchangedCount;
        }
    }
}
