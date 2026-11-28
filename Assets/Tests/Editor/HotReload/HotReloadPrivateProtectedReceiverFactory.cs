using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Returns the derived fixtures the partial-type tests edit, from a file those tests never give
    /// the reload, so the returned value is typed as the compiled copy of the edited type.
    /// </summary>
    public static class HotReloadPrivateProtectedReceiverFactory
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static HotReloadPlainDerivedFixture CreatePlain()
        {
            return new HotReloadPlainDerivedFixture();
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public static HotReloadPartialDerivedFixture CreatePartial()
        {
            return new HotReloadPartialDerivedFixture();
        }
    }
}
