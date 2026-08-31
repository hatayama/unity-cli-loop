using System;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Absolute paths of snapshot-mismatched sibling sources, plus a cap warning when truncated.
    /// </summary>
    internal sealed class HotReloadChangedSiblingScanResult
    {
        internal static readonly HotReloadChangedSiblingScanResult Empty =
            new HotReloadChangedSiblingScanResult(Array.Empty<string>(), string.Empty);

        internal string[] ChangedSiblingAbsolutePaths { get; }

        internal string ScanLimitWarning { get; }

        internal HotReloadChangedSiblingScanResult(string[] changedSiblingAbsolutePaths, string scanLimitWarning)
        {
            ChangedSiblingAbsolutePaths = changedSiblingAbsolutePaths ?? Array.Empty<string>();
            ScanLimitWarning = scanLimitWarning ?? string.Empty;
        }
    }
}
