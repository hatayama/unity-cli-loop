using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides on the main thread what the warm-up loads, or why it loads nothing.
    /// </summary>
    internal interface IHotReloadWarmUpContextSource
    {
        HotReloadWarmUpCapture Capture();
    }

    /// <summary>
    /// Either a context to warm up or the reason there is none; exactly one of the two is set.
    /// </summary>
    internal sealed class HotReloadWarmUpCapture
    {
        internal const string SkipReasonNoTargets = "no_targets";
        internal const string SkipReasonCompiling = "compiling";
        internal const string SkipReasonUpdating = "updating";
        internal const string SkipReasonNoCompiledAssembly = "no_compiled_assembly";

        private HotReloadWarmUpCapture(HotReloadWarmUpContext context, string skipReason)
        {
            Context = context;
            SkipReason = skipReason;
        }

        internal HotReloadWarmUpContext Context { get; }

        internal string SkipReason { get; }

        internal static HotReloadWarmUpCapture Ready(HotReloadWarmUpContext context)
        {
            Debug.Assert(context != null, "context must not be null.");
            return new HotReloadWarmUpCapture(context, null);
        }

        internal static HotReloadWarmUpCapture Skipped(string reason)
        {
            Debug.Assert(!string.IsNullOrEmpty(reason), "reason must not be null or empty.");
            return new HotReloadWarmUpCapture(null, reason);
        }
    }
}
