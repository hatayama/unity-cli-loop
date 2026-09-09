using System.Collections.Generic;
using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test double for the pause-point side of the coordination point. Every member falls through
    /// to <see cref="Inner"/> (the port that was installed when the test opened) unless the test
    /// overrides that one member.
    /// </summary>
    public sealed class StubPausePointHotReloadPort : IPausePointHotReloadPort
    {
        /// <summary>The port answers fall through to. Null answers "pause point not initialized".</summary>
        public IPausePointHotReloadPort Inner { get; set; }

        public System.Func<MethodBase, IReadOnlyList<string>> ArmedMarkerIdsOnMethod { get; set; }

        public System.Func<MethodBase, IReadOnlyList<string>> SuppressedMarkerIdsOnMethod { get; set; }

        public System.Func<IReadOnlyList<string>> ExpiredNotRetargetedMarkerIds { get; set; }

        public System.Func<IReadOnlyList<(string Id, string OldText, string NewText)>>
            RetargetLineDriftWarnings { get; set; }

        public System.Action<MethodBase, bool> HotReloadPatchStateChanged { get; set; }

        public IReadOnlyList<string> GetArmedMarkerIdsOnMethod(MethodBase method)
        {
            return ArmedMarkerIdsOnMethod != null
                ? ArmedMarkerIdsOnMethod(method)
                : Inner?.GetArmedMarkerIdsOnMethod(method);
        }

        public IReadOnlyList<string> GetSuppressedMarkerIdsOnMethod(MethodBase method)
        {
            return SuppressedMarkerIdsOnMethod != null
                ? SuppressedMarkerIdsOnMethod(method)
                : Inner?.GetSuppressedMarkerIdsOnMethod(method);
        }

        public IReadOnlyList<string> ConsumeExpiredNotRetargetedMarkerIds()
        {
            return ExpiredNotRetargetedMarkerIds != null
                ? ExpiredNotRetargetedMarkerIds()
                : Inner?.ConsumeExpiredNotRetargetedMarkerIds();
        }

        public IReadOnlyList<(string Id, string OldText, string NewText)> ConsumeRetargetLineDriftWarnings()
        {
            return RetargetLineDriftWarnings != null
                ? RetargetLineDriftWarnings()
                : Inner?.ConsumeRetargetLineDriftWarnings();
        }

        public void OnHotReloadPatchStateChanged(MethodBase method, bool patched)
        {
            if (HotReloadPatchStateChanged != null)
            {
                HotReloadPatchStateChanged(method, patched);
                return;
            }

            Inner?.OnHotReloadPatchStateChanged(method, patched);
        }
    }
}
