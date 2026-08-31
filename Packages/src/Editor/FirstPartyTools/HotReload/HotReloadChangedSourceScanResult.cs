using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Changed project-relative source paths and baseline availability for one compilation assembly.
    /// </summary>
    internal sealed class HotReloadChangedSourceScanResult
    {
        internal bool HasBaseline { get; }

        internal List<string> ChangedProjectRelativePaths { get; }

        internal string ScanLimitWarning { get; }

        internal HotReloadChangedSourceScanResult(
            bool hasBaseline,
            List<string> changedProjectRelativePaths,
            string scanLimitWarning)
        {
            HasBaseline = hasBaseline;
            ChangedProjectRelativePaths = changedProjectRelativePaths ?? new List<string>();
            ScanLimitWarning = scanLimitWarning ?? string.Empty;
        }
    }
}
