using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// The other part of <see cref="HotReloadPartialInternalPeer"/>, holding the internal method the
    /// visibility repro tests call.
    /// </summary>
    public partial class HotReloadPartialInternalPeer
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal int PeerInternalValue()
        {
            return 11;
        }
    }
}
