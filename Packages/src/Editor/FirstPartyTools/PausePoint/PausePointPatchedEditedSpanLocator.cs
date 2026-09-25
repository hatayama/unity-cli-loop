using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Finds hot-reload patched methods' edited-file spans, by a line they hold or by the method,
    /// while the file on disk is still the source the hot reload compiled.
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

        // Why no freshness check here, unlike the span lookups: the caller asks whether the latest
        // shim generation holds the method at all, and has already checked that its source is
        // the file on disk.
        internal static bool IsMethodInShimLookup(string file, MethodBase method)
        {
            IHotReloadPausePointPort hotReloadSide = HotReloadPausePointCoordination.HotReloadSide;
            if (string.IsNullOrEmpty(file) || method == null || hotReloadSide == null)
            {
                return false;
            }

            HotReloadShimFileLookup lookup =
                hotReloadSide.GetShimLookupForFile(SourcePausePointPathNormalizer.ToForwardSlashes(file));
            if (lookup?.Methods == null)
            {
                return false;
            }

            foreach (HotReloadShimMethodLookup entry in lookup.Methods)
            {
                if (entry.OriginalMethod == method)
                {
                    return true;
                }
            }

            return false;
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

        // Why no spans once the file changed on disk: they are lines of the source the hot reload
        // compiled, so compared with edited-file lines they would place a line in a method it is
        // not part of. Callers fall back to text without a range, or to refusals whose next action
        // is a hot reload, which records spans for the file on disk again.
        private static HotReloadShimFileLookup FindShimLookupOrNull(string file)
        {
            IHotReloadPausePointPort hotReloadSide = HotReloadPausePointCoordination.HotReloadSide;
            string forwardSlashFile = SourcePausePointPathNormalizer.ToForwardSlashes(file);
            if (hotReloadSide == null || hotReloadSide.HasShimSourceChangedOnDisk(forwardSlashFile))
            {
                return null;
            }

            HotReloadShimFileLookup lookup = hotReloadSide.GetShimLookupForFile(forwardSlashFile);
            return lookup?.Methods == null ? null : lookup;
        }

        private static bool HasEditedSpan(HotReloadShimMethodLookup entry)
        {
            return entry.SourceStartLine > 0 && entry.SourceEndLine >= entry.SourceStartLine;
        }
    }
}
