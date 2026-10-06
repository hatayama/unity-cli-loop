using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// A second type declaring a method named like HotReloadCoreFixture.StaticPing, so a
    /// test can tell a peel that matches by name alone from one that resolves the method.
    /// </summary>
    public static class HotReloadPeelSameNameFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public static string StaticPing()
        {
            return "same-name";
        }
    }
}
