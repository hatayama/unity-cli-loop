using System;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Editor-domain coordination point for the tools that warn about what a domain reload is
    /// about to discard. The hot-reload tool lives in a sibling assembly those tools must not
    /// reference, so it publishes the count through a delegate wired at its own startup.
    /// </summary>
    /// <remarks>
    /// Why this is separate from <see cref="HotReloadPausePointCoordination"/>: that class
    /// coordinates hot reload with the pause-point tool, which asks about method shims only. What
    /// a domain reload discards is a different question with different consumers, and its answer
    /// includes the types a reload introduced, which own no shim.
    /// </remarks>
    public static class HotReloadRuntimeChangeCoordination
    {
        // Set by the hot-reload startup. Returns how many hot-reload changes the next domain
        // reload discards: patched methods, added members, and introduced types. Null means the
        // hot-reload tool has not initialized in this domain, which also means none exist -
        // callers treat null as 0.
        public static Func<int> GetActiveRuntimeChangeCount { get; set; }
    }
}
