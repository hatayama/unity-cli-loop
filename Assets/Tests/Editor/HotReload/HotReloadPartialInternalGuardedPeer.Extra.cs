#if UNITY_EDITOR
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The other part of <see cref="HotReloadPartialInternalGuardedPeer"/>. The whole file sits in a
    /// conditional-compilation block, the way an editor-only or debug-only part usually does.
    /// </summary>
    public partial class HotReloadPartialInternalGuardedPeer
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal int GuardedPeerInternalValue()
        {
            return 12;
        }
    }
}
#endif
