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
            HotReloadSnapshotMissReason snapshotMissReason,
            bool declaresIntroducedType,
            bool declaresRefusedIntroducedType,
            string projectRelativePath,
            string assemblyName,
            string assemblyResolvePath,
            List<HotReloadMethodOutcome> outcomes,
            List<string> warnings,
            HotReloadSiblingBaselineNotices siblingBaselineNotices)
        {
            Debug.Assert(fileOutput != null, "fileOutput must not be null.");
            Debug.Assert(fileSkipped != null, "fileSkipped must not be null.");
            Debug.Assert(patchCandidateRowCountForFile >= 0, "patchCandidateRowCountForFile must not be negative.");
            Debug.Assert(outcomes != null, "outcomes must not be null.");
            Debug.Assert(warnings != null, "warnings must not be null.");

            AppendBaselineNotices(
                fileOutput,
                patchCandidateRowCountForFile,
                snapshotMissReason,
                declaresIntroducedType,
                declaresRefusedIntroducedType,
                projectRelativePath,
                assemblyName,
                warnings,
                siblingBaselineNotices);
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
            HotReloadSnapshotMissReason snapshotMissReason,
            bool declaresIntroducedType,
            bool declaresRefusedIntroducedType,
            string projectRelativePath,
            string assemblyName,
            List<string> warnings,
            HotReloadSiblingBaselineNotices siblingBaselineNotices)
        {
            // Why a refused declaration silences it: the file's type notice already says only a
            // compile makes that type available, and "patching all methods" beside it reads as if
            // the file were patched while the compile it needs also establishes the baseline.
            if (snapshotMissReason != HotReloadSnapshotMissReason.None
                && patchCandidateRowCountForFile >= 1
                && !declaresRefusedIntroducedType)
            {
                HotReloadMissingBaselineKind kind =
                    ChooseMissingBaselineKind(snapshotMissReason, declaresIntroducedType);
                if (siblingBaselineNotices != null)
                {
                    siblingBaselineNotices.Add(kind, projectRelativePath);
                }
                else
                {
                    warnings.Add(
                        string.Format(
                            ChooseMissingBaselineWarningFormat(kind),
                            Path.GetFileName(projectRelativePath),
                            assemblyName));
                }
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

        /// <summary>
        /// Why this file has no verified snapshot. A file declaring a type hot reload introduced
        /// gets its own kind: for it a missing baseline is the normal state rather than something
        /// a compile has yet to establish. So does a file with no compiled method body, which the
        /// PDB never lists and so no compile can give a baseline.
        /// </summary>
        private static HotReloadMissingBaselineKind ChooseMissingBaselineKind(
            HotReloadSnapshotMissReason snapshotMissReason,
            bool declaresIntroducedType)
        {
            if (declaresIntroducedType)
            {
                return HotReloadMissingBaselineKind.IntroducedType;
            }

            return snapshotMissReason == HotReloadSnapshotMissReason.NoDocumentInPdb
                ? HotReloadMissingBaselineKind.NoCompiledMethodBody
                : HotReloadMissingBaselineKind.NoVerifiedSourceSnapshot;
        }

        private static string ChooseMissingBaselineWarningFormat(HotReloadMissingBaselineKind kind)
        {
            if (kind == HotReloadMissingBaselineKind.IntroducedType)
            {
                return HotReloadConstants.IntroducedTypeSourceNoBaselineWarningFormat;
            }

            return kind == HotReloadMissingBaselineKind.NoCompiledMethodBody
                ? HotReloadConstants.NoCompiledMethodBodyBaselineWarningFormat
                : HotReloadConstants.NoVerifiedSourceSnapshotWarningFormat;
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
                        HotReloadWorkerReasonText.Render(skipped.reason),
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
