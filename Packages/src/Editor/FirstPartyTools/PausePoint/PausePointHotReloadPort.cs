using System.Collections.Generic;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Answers the hot-reload tool's questions about the markers pause point has injected in this
    /// domain. Published by <see cref="SourcePausePointPatcher"/> when it initializes.
    /// </summary>
    internal sealed class PausePointHotReloadPort : IPausePointHotReloadPort
    {
        public IReadOnlyList<string> GetArmedMarkerIdsOnMethod(MethodBase method)
        {
            return SourcePausePointHotReloadRetarget.GetArmedMarkerIds(method);
        }

        public IReadOnlyList<string> GetSuppressedMarkerIdsOnMethod(MethodBase method)
        {
            return SourcePausePointHotReloadRetarget.GetSuppressedMarkerIds(method);
        }

        public IReadOnlyList<string> ConsumeExpiredNotRetargetedMarkerIds()
        {
            return SourcePausePointHotReloadRetarget.ConsumeExpiredNotRetargetedMarkerIds();
        }

        public IReadOnlyList<(string Id, string OldText, string NewText)> ConsumeRetargetLineDriftWarnings()
        {
            return SourcePausePointHotReloadRetarget.ConsumeRetargetLineDriftWarnings();
        }

        public void OnHotReloadPatchStateChanged(MethodBase method, bool patched)
        {
            SourcePausePointHotReloadRetarget.HandleHotReloadPatchStateChanged(method, patched);
        }
    }
}
