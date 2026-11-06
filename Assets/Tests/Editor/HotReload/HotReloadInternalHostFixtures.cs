using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A host the shim assembly cannot name, for the tests that edit a closure or an async body
    /// which reaches only members the same reload adds.
    /// </summary>
    internal static class HotReloadInternalStaticHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        internal static int Count(int threshold)
        {
            return -1;
        }
    }

    /// <summary>
    /// The instance form of <see cref="HotReloadInternalStaticHost"/>: its closures capture a
    /// receiver whose type the shim assembly cannot name.
    /// </summary>
    internal sealed class HotReloadInternalInstanceHost
    {
        internal int Offset = 10;

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal int Count(int threshold)
        {
            return -1;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        internal Task<int> CountAsync(int threshold)
        {
            return Task.FromResult(-1);
        }
    }

    /// <summary>
    /// A type the shim assembly can name, holding a member it cannot, for the test that reaches an
    /// accessor whose own signature is visible from a host that is not.
    /// </summary>
    public sealed class HotReloadInternalHostVisibleNeighbor
    {
        internal int Hidden = 3;
    }
}
