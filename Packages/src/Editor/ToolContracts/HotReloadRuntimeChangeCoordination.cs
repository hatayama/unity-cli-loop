using System;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Editor-domain coordination point for the tools that warn about what a domain reload is
    /// about to discard. The hot-reload tool lives in a sibling assembly those tools must not
    /// reference, so it publishes the count through a delegate wired at its own startup.
    /// </summary>
    public static class HotReloadRuntimeChangeCoordination
    {
        // Set by the hot-reload startup. Returns how many hot-reload changes the next domain
        // reload discards: patched methods, added members, and introduced types. Null means the
        // hot-reload tool has not initialized in this domain, which also means none exist -
        // callers treat null as 0.
        public static Func<int> GetActiveRuntimeChangeCount { get; set; }
    }
}
