using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// An ordinary method and, on the next line, an explicit interface implementation. When both
    /// are hot reloaded, a pause point on the second line must arm the explicit implementation,
    /// not the method declared just above it.
    /// </summary>
    public class HotReloadPausePointExplicitFixture : IHotReloadExplicitRunner
    {
        // Why attribute and body on one line: the two methods must sit on adjacent source lines.
        [MethodImpl(MethodImplOptions.NoInlining)] public int First() { return 4201; }
        [MethodImpl(MethodImplOptions.NoInlining)] int IHotReloadExplicitRunner.Run() { return 4202; }
    }
}
