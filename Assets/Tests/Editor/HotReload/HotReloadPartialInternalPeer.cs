using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The main part of a partial type whose internal method lives in another file. No test passes
    /// this file, so the worker sees the type as compiled.
    /// </summary>
    public partial class HotReloadPartialInternalPeer
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PeerMain()
        {
            return 1;
        }
    }

    /// <summary>
    /// The main part of a partial type whose internal method lives in a file wrapped in a
    /// conditional-compilation block.
    /// </summary>
    public partial class HotReloadPartialInternalGuardedPeer
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int GuardedPeerMain()
        {
            return 2;
        }
    }
}
