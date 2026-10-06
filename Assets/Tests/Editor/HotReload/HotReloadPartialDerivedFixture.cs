using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A partial type deriving from <see cref="HotReloadInternalMemberHost"/>, so its edited bodies can
    /// reach the protected members of a compiled base type.
    /// </summary>
    public partial class HotReloadPartialDerivedFixture : HotReloadInternalMemberHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DerivedValue()
        {
            return 9;
        }
    }
}
