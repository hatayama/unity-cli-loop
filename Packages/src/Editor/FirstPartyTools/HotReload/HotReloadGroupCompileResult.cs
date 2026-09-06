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
            HotReloadGroupCompileOutcome outcome,
            TransformWorkerEntryDto[] entriesToPatch,
            HotReloadShimCompileResult compileResult)
        {
            Outcome = outcome;
            EntriesToPatch = entriesToPatch;
            CompileResult = compileResult;
        }

        internal HotReloadGroupCompileOutcome Outcome { get; }

        // False when the outcomes of every file are already in their sinks: the group cleared
        // its generations, failed, or produced no entry to patch.
        internal bool HasEntriesToApply => Outcome == HotReloadGroupCompileOutcome.ReadyWithMethods;

        internal TransformWorkerEntryDto[] EntriesToPatch { get; }

        internal HotReloadShimCompileResult CompileResult { get; }

        /// <summary>
        /// The group cannot apply anything because a failure or a skip already took its entries.
        /// </summary>
        internal static HotReloadGroupCompileResult Failed()
        {
            return new HotReloadGroupCompileResult(HotReloadGroupCompileOutcome.Failed, null, null);
        }

        /// <summary>
        /// The group succeeded and simply has no method left to patch.
        /// </summary>
        internal static HotReloadGroupCompileResult ReadyWithoutMethods()
        {
            return new HotReloadGroupCompileResult(HotReloadGroupCompileOutcome.ReadyWithoutMethods, null, null);
        }

        /// <summary>
        /// The group compiled its shim and holds the entries the apply stage patches.
        /// </summary>
        internal static HotReloadGroupCompileResult ReadyWithMethods(
            TransformWorkerEntryDto[] entriesToPatch,
            HotReloadShimCompileResult compileResult)
        {
            Debug.Assert(entriesToPatch != null, "entriesToPatch must not be null.");
            Debug.Assert(entriesToPatch.Length > 0, "An apply must hold an entry.");
            Debug.Assert(compileResult != null, "compileResult must not be null.");
            return new HotReloadGroupCompileResult(
                HotReloadGroupCompileOutcome.ReadyWithMethods,
                entriesToPatch,
                compileResult);
        }
    }
}
