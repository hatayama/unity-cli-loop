using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Absolute paths of snapshot-mismatched sibling sources, plus a cap warning when truncated.
    /// </summary>
    internal sealed class HotReloadChangedSiblingScanResult
    {
        // Why incomplete: Empty stands for a scan that never ran (no DLL, PDB or source list), and
        // an empty list from a scan that never ran does not mean that no sibling changed.
        internal static readonly HotReloadChangedSiblingScanResult Empty =
            new HotReloadChangedSiblingScanResult(Array.Empty<string>(), string.Empty, isComplete: false);

        internal string[] ChangedSiblingAbsolutePaths { get; }

        internal string ScanLimitWarning { get; }

        /// <summary>
        /// True only when every sibling was compared with its snapshot and the list was not
        /// truncated, so a sibling missing from the list is known to be unchanged.
        /// </summary>
        internal bool IsComplete { get; }

        internal HotReloadChangedSiblingScanResult(
            string[] changedSiblingAbsolutePaths,
            string scanLimitWarning,
            bool isComplete)
        {
            ChangedSiblingAbsolutePaths = changedSiblingAbsolutePaths ?? Array.Empty<string>();
            ScanLimitWarning = scanLimitWarning ?? string.Empty;
            IsComplete = isComplete;
        }
    }
}
