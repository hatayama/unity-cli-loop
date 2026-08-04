using System;
using System.Collections.Generic;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// Editor-domain coordination point between the hot-reload tool and the source
    /// pause-point tool. The two tools live in sibling assemblies that must not
    /// reference each other, so each side publishes its state through delegates
    /// wired in its own static constructor (the same pattern as
    /// UloopPausePointRegistry's OnCleared wiring). A null delegate means the owning
    /// side has not initialized in this domain, which also means it has no state
    /// worth querying; callers treat null as "no patches" / "no markers".
    /// </summary>
    public static class HotReloadPausePointCoordination
    {
        // Set by HotReloadPatcher. True while the method's body is replaced by a
        // hot-reload patch.
        public static Func<MethodBase, bool> IsMethodPatchedByHotReload { get; set; }

        // Set by SourcePausePointPatcher. Returns the marker ids currently injected
        // into the method (empty when none).
        public static Func<MethodBase, IReadOnlyList<string>> GetArmedMarkerIdsOnMethod { get; set; }

        // Set by SourcePausePointPatcher. Invoked by HotReloadPatcher after a
        // method's patch state changes (true = patched, false = reverted).
        public static Action<MethodBase, bool> OnHotReloadPatchStateChanged { get; set; }
    }
}
