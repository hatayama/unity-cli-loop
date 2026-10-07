using System.Runtime.CompilerServices;

using io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload.ShimReferenceForeignInternal;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Calls extension methods that a referenced assembly declares twice: publicly, and on an
    /// internal type or as an internal member. This assembly's compile sees only the public ones, so
    /// a shim compile that also sees the internal ones finds each call ambiguous.
    /// </summary>
    public class HotReloadShimReferenceForeignInternalFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int TripleViaExtension(int value)
        {
            return value.Tripled();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int QuadrupleViaExtension(int value)
        {
            return value.Quadrupled();
        }
    }
}
