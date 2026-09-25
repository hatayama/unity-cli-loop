using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds the hot-reload patched method whose edited-file span holds a line.
    /// </summary>
    internal static class PausePointPatchedEditedSpanLocator
    {
        internal static string FindPatchedMethodContainingEditedLineOrNull(string file, int line)
        {
            return FindPatchedSpanContainingEditedLineOrNull(file, line)?.Label;
        }

        // Why read the port here instead of taking the lookup as an argument: the enable use
        // case already fetched it, but threading it through would grow a file kept near its
        // length limit, and the lookup is cheap to fetch again.
        internal static PausePointPatchedEditedSpan FindPatchedSpanContainingEditedLineOrNull(string file, int line)
        {
            if (string.IsNullOrEmpty(file) || line <= 0)
            {
                return null;
            }

            HotReloadShimFileLookup lookup = HotReloadPausePointCoordination.HotReloadSide?.GetShimLookupForFile(
                SourcePausePointPathNormalizer.ToForwardSlashes(file));
            if (lookup == null || lookup.Methods == null)
            {
                return null;
            }

            foreach (HotReloadShimMethodLookup entry in lookup.Methods)
            {
                if (entry.SourceStartLine <= 0 || entry.SourceEndLine < entry.SourceStartLine)
                {
                    continue;
                }

                if (line < entry.SourceStartLine || line > entry.SourceEndLine)
                {
                    continue;
                }

                return new PausePointPatchedEditedSpan(
                    DescribeMethod(entry.OriginalMethod),
                    entry.SourceStartLine,
                    entry.SourceEndLine);
            }

            return null;
        }

        private static string DescribeMethod(MethodBase method)
        {
            if (method == null)
            {
                return "?";
            }

            return method.DeclaringType != null
                ? method.DeclaringType.Name + "." + method.Name
                : "?." + method.Name;
        }
    }
}
