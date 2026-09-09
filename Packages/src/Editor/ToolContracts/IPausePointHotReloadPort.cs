using System.Collections.Generic;
using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.ToolContracts
{
    /// <summary>
    /// What the hot-reload tool may ask the pause-point tool about the markers it currently has
    /// injected. Implemented by pause point, read through
    /// <see cref="HotReloadPausePointCoordination.PausePointSide"/>.
    /// </summary>
    public interface IPausePointHotReloadPort
    {
        /// <summary>
        /// Returns the marker ids currently injected into the method (empty when none).
        /// </summary>
        IReadOnlyList<string> GetArmedMarkerIdsOnMethod(MethodBase method);

        /// <summary>
        /// Returns marker ids whose logical owner is the method and whose registry entry is
        /// currently SuppressedByHotReload.
        /// </summary>
        IReadOnlyList<string> GetSuppressedMarkerIdsOnMethod(MethodBase method);

        /// <summary>
        /// Drains marker ids recorded during the latest hot-reload patch transition that were
        /// skipped for retarget because they were already Expired (not a scan of residual expired
        /// ledger state).
        /// </summary>
        IReadOnlyList<string> ConsumeExpiredNotRetargetedMarkerIds();

        /// <summary>
        /// Drains (id, oldText, newText) triples recorded when retarget changed the resolved line
        /// text of an armed marker.
        /// </summary>
        IReadOnlyList<(string Id, string OldText, string NewText)> ConsumeRetargetLineDriftWarnings();

        /// <summary>
        /// Called by the hot-reload patcher after a method's patch state changes
        /// (true = patched, false = reverted).
        /// </summary>
        void OnHotReloadPatchStateChanged(MethodBase method, bool patched);
    }
}
