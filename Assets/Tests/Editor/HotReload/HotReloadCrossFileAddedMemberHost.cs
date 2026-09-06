using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host type for cross-file worker tests. Tests copy this source, add members to the
    /// copy, and expect a body edited in the sibling caller file to bind against them.
    /// </summary>
    // Why public: accessor-delegate plans compile in a separate shim assembly.
    public sealed class HotReloadCrossFileAddedMemberHost
    {
        private int _stored;

        public int Value()
        {
            return 1;
        }

        // Why NoInlining: end-to-end tests edit this body and read the new value back through a
        // direct call, which an inlined copy at the call site would not observe.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Scaled(int factor)
        {
            return factor;
        }

        // Why a member nothing else calls: the signature-change gate demands every compiled call
        // site of a replaced method be patched in the same reload, so this one is called only
        // from the sibling caller fixture.
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Gated(int seed)
        {
            return seed;
        }
    }
}
