using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A plain type deriving from <see cref="HotReloadInternalMemberHost"/>: the control for the
    /// partial type that derives from it.
    /// </summary>
    public class HotReloadPlainDerivedFixture : HotReloadInternalMemberHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int DerivedValue()
        {
            return 10;
        }
    }
}
