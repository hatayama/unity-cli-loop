using System;
using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Files and response details chosen when callers omit the hot-reload files parameter.
    /// </summary>
    internal sealed class HotReloadDefaultFileSelection
    {
        internal IReadOnlyList<string> Files { get; }

        internal IReadOnlyList<string> ScanLimitWarnings { get; }

        internal string SelectionMessage { get; }

        internal HotReloadValidationFailure ValidationFailure { get; }

        internal HotReloadDefaultFileSelection(
            IReadOnlyList<string> files,
            IReadOnlyList<string> scanLimitWarnings,
            string selectionMessage,
            HotReloadValidationFailure validationFailure)
        {
            Files = files ?? Array.Empty<string>();
            ScanLimitWarnings = scanLimitWarnings ?? Array.Empty<string>();
            SelectionMessage = selectionMessage ?? string.Empty;
            ValidationFailure = validationFailure;
        }
    }

    /// <summary>
    /// Resolves omitted hot-reload files from compile snapshots without mixing selection with apply execution.
    /// </summary>
    internal static class HotReloadDefaultFileSelector
    {
        internal static HotReloadDefaultFileSelection Resolve(
            string[] files,
            Func<HotReloadChangedFileAggregationResult> changedFileDetector,
            IReadOnlyList<string> droppedIntroducedSourcePaths)
        {
            Debug.Assert(changedFileDetector != null, "changedFileDetector must not be null.");
            Debug.Assert(droppedIntroducedSourcePaths != null, "droppedIntroducedSourcePaths must not be null.");

            if (files != null && files.Length > 0)
            {
                return new HotReloadDefaultFileSelection(
                    files,
                    Array.Empty<string>(),
                    string.Empty,
                    validationFailure: null);
            }

            return SelectChangedFiles(changedFileDetector(), droppedIntroducedSourcePaths);
        }

        private static HotReloadDefaultFileSelection SelectChangedFiles(
            HotReloadChangedFileAggregationResult changedFiles,
            IReadOnlyList<string> droppedIntroducedSourcePaths)
        {
            Debug.Assert(changedFiles != null, "changedFiles must not be null.");

            if (!changedFiles.HasBaseline)
            {
                return new HotReloadDefaultFileSelection(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    string.Empty,
                    new HotReloadValidationFailure(
                        HotReloadConstants.NoCompileSnapshotsMessage,
                        HotReloadValidationErrorCodes.FilesRequired,
                        new[]
                        {
                            "Run 'uloop compile' to create source snapshots.",
                            HotReloadConstants.PassExplicitFilesNextAction
                        }));
            }

            IReadOnlyList<string> reselectedPaths = ExcludeChangedPaths(
                droppedIntroducedSourcePaths,
                changedFiles.ChangedProjectRelativePaths);
            if (changedFiles.ChangedProjectRelativePaths.Count == 0 && reselectedPaths.Count == 0)
            {
                return new HotReloadDefaultFileSelection(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    string.Empty,
                    new HotReloadValidationFailure(
                        HotReloadConstants.NoChangedFilesMessage,
                        HotReloadValidationErrorCodes.NoChangedFiles,
                        new[]
                        {
                            "Save the edited .cs files to disk, then run 'uloop hot-reload' again.",
                            HotReloadConstants.PassExplicitFilesNextAction
                        }));
            }

            List<string> selectedFiles = new List<string>(changedFiles.ChangedProjectRelativePaths);
            selectedFiles.AddRange(reselectedPaths);
            return new HotReloadDefaultFileSelection(
                selectedFiles,
                changedFiles.ScanLimitWarnings,
                BuildSelectionMessage(changedFiles.ChangedProjectRelativePaths, reselectedPaths),
                validationFailure: null);
        }

        // A discarded owner file the user has since edited is already a changed file, so only
        // the rest are added, in the order the caller listed them.
        private static IReadOnlyList<string> ExcludeChangedPaths(
            IReadOnlyList<string> droppedIntroducedSourcePaths,
            IReadOnlyList<string> changedPaths)
        {
            HashSet<string> changed = new HashSet<string>(changedPaths, StringComparer.Ordinal);
            List<string> reselected = new List<string>();
            for (int index = 0; index < droppedIntroducedSourcePaths.Count; index++)
            {
                string path = droppedIntroducedSourcePaths[index];
                if (changed.Add(path))
                {
                    reselected.Add(path);
                }
            }

            return reselected;
        }

        private static string BuildSelectionMessage(
            IReadOnlyList<string> changedPaths,
            IReadOnlyList<string> reselectedPaths)
        {
            string changedPart = changedPaths.Count == 0
                ? HotReloadConstants.DefaultSelectionNoChangedFilesPrefix
                : "--files was omitted; "
                    + changedPaths.Count
                    + " changed file(s) since the last compile were selected: "
                    + string.Join(", ", changedPaths)
                    + ".";
            if (reselectedPaths.Count == 0)
            {
                return changedPart + HotReloadConstants.DefaultSelectionNewFilesNote;
            }

            return changedPart
                + string.Format(
                    HotReloadConstants.DefaultSelectionReselectedDroppedFilesFormat,
                    reselectedPaths.Count,
                    string.Join(", ", reselectedPaths))
                + HotReloadConstants.DefaultSelectionOtherNewFilesNote;
        }
    }
}
