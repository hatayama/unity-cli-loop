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
            Func<HotReloadChangedFileAggregationResult> changedFileDetector)
        {
            Debug.Assert(changedFileDetector != null, "changedFileDetector must not be null.");

            if (files != null && files.Length > 0)
            {
                return new HotReloadDefaultFileSelection(
                    files,
                    Array.Empty<string>(),
                    string.Empty,
                    validationFailure: null);
            }

            return SelectChangedFiles(changedFileDetector());
        }

        private static HotReloadDefaultFileSelection SelectChangedFiles(
            HotReloadChangedFileAggregationResult changedFiles)
        {
            Debug.Assert(changedFiles != null, "changedFiles must not be null.");

            if (!changedFiles.HasBaseline)
            {
                return new HotReloadDefaultFileSelection(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    string.Empty,
                    new HotReloadValidationFailure(
                        "No compile snapshots exist yet. Run 'uloop compile' first or pass project-relative .cs paths with --files.",
                        HotReloadValidationErrorCodes.FilesRequired,
                        new[]
                        {
                            "Run 'uloop compile' to create source snapshots.",
                            "Pass project-relative .cs paths with --files."
                        }));
            }

            if (changedFiles.ChangedProjectRelativePaths.Count == 0)
            {
                return new HotReloadDefaultFileSelection(
                    Array.Empty<string>(),
                    Array.Empty<string>(),
                    string.Empty,
                    new HotReloadValidationFailure(
                        "No .cs files changed since the last compile were found; pass explicit paths with --files.",
                        HotReloadValidationErrorCodes.NoChangedFiles,
                        new[]
                        {
                            "Save the edited .cs files to disk, then run 'uloop hot-reload' again.",
                            "Pass project-relative .cs paths with --files."
                        }));
            }

            string selectionMessage = "--files was omitted; "
                + changedFiles.ChangedProjectRelativePaths.Count
                + " changed file(s) since the last compile were selected: "
                + string.Join(", ", changedFiles.ChangedProjectRelativePaths)
                + ".";
            return new HotReloadDefaultFileSelection(
                changedFiles.ChangedProjectRelativePaths,
                changedFiles.ScanLimitWarnings,
                selectionMessage,
                validationFailure: null);
        }
    }
}
