using System.Collections.Generic;

using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// What the group's shim compile left for the apply stage: the entries to patch against the
    /// compiled shim assembly, or nothing at all when every file already reported its result.
    /// </summary>
    internal sealed class HotReloadGroupCompileResult
    {
        private HotReloadGroupCompileResult(
            TransformWorkerEntryDto[] entriesToPatch,
            HotReloadShimCompileResult compileResult)
        {
            EntriesToPatch = entriesToPatch;
            CompileResult = compileResult;
        }

        // False when the outcomes of every file are already in their sinks: the group cleared
        // its generations, failed, or produced no entry to patch.
        internal bool HasEntriesToApply => EntriesToPatch != null;

        internal TransformWorkerEntryDto[] EntriesToPatch { get; }

        internal HotReloadShimCompileResult CompileResult { get; }

        internal static HotReloadGroupCompileResult NothingToApply()
        {
            return new HotReloadGroupCompileResult(null, null);
        }

        internal static HotReloadGroupCompileResult Apply(
            TransformWorkerEntryDto[] entriesToPatch,
            HotReloadShimCompileResult compileResult)
        {
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");
            Debug.Assert(entriesToPatch.Length > 0, "An apply must hold an entry.");
            Debug.Assert(compileResult != null, "compileResult must not be null.");
            return new HotReloadGroupCompileResult(entriesToPatch, compileResult);
        }
    }
}
