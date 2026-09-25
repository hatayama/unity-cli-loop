using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds hot-reload patched methods' edited-file spans, by a line they hold or by the method.
    /// </summary>
    internal static class PausePointPatchedEditedSpanLocator
    {
        // Why read the port here instead of taking the lookup as an argument: the enable use
        // case already fetched it, but threading it through would grow a file kept near its
        // length limit, and the lookup is cheap to fetch again.
        internal static PausePointPatchedEditedSpan FindPatchedSpanContainingEditedLineOrNull(string file, int line)
        {
            if (string.IsNullOrEmpty(file) || line <= 0)
            {
                return null;
            }

            HotReloadShimFileLookup lookup = FindShimLookupOrNull(file);
            if (lookup == null)
            {
                return null;
            }

            foreach (HotReloadShimMethodLookup entry in lookup.Methods)
            {
                if (!HasEditedSpan(entry))
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

        // Why skip an entry without a valid span: a 0-0 range in the refusal would send the caller
        // to a line that is not in the patched body, so the caller falls back to range-less text.
        internal static PausePointPatchedEditedSpan FindPatchedSpanOfMethodOrNull(string file, MethodBase method)
        {
            if (string.IsNullOrEmpty(file) || method == null)
            {
                return null;
            }

            HotReloadShimFileLookup lookup = FindShimLookupOrNull(file);
            if (lookup == null)
            {
                return null;
            }

            foreach (HotReloadShimMethodLookup entry in lookup.Methods)
            {
                if (entry.OriginalMethod != method || !HasEditedSpan(entry))
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

        internal static string DescribeMethod(MethodBase method)
        {
            if (method == null)
            {
                return "?";
            }

            return method.DeclaringType != null
                ? method.DeclaringType.Name + "." + method.Name
                : "?." + method.Name;
        }

        private static HotReloadShimFileLookup FindShimLookupOrNull(string file)
        {
            HotReloadShimFileLookup lookup = HotReloadPausePointCoordination.HotReloadSide?.GetShimLookupForFile(
                SourcePausePointPathNormalizer.ToForwardSlashes(file));
            return lookup?.Methods == null ? null : lookup;
        }

        private static bool HasEditedSpan(HotReloadShimMethodLookup entry)
        {
            return entry.SourceStartLine > 0 && entry.SourceEndLine >= entry.SourceStartLine;
        }
    }
}
