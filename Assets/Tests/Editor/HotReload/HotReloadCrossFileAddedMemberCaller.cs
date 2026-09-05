using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled caller type for cross-file worker tests. Tests edit a copy of this body so it
    /// calls a member added in the sibling host file within the same worker run.
    /// </summary>
    internal sealed class HotReloadCrossFileAddedMemberCaller
    {
        // Why NoInlining on every member: end-to-end tests read patched bodies back through a
        // direct call, which an inlined copy at the call site would not observe.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Call(HotReloadCrossFileAddedMemberHost host)
        {
            return host.Value();
        }

        // Second editable member of this file, so a file-atomic skip has something to report
        // besides the method a compile error was attributed to.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Other()
        {
            return 7;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallScaled(HotReloadCrossFileAddedMemberHost host)
        {
            return host.Scaled(1);
        }

        // The only compiled call site of the host's gated member.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallGated(HotReloadCrossFileAddedMemberHost host)
        {
            return host.Gated(1);
        }
    }
}
