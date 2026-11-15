using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Two one-line methods on adjacent lines. When both are hot reloaded, the closing brace of the
    /// first method's shim maps onto the second method's line, which is what a pause point on the
    /// second line must not be resolved to.
    /// </summary>
    public class HotReloadPausePointAdjacentMethodsFixture
    {
        // Why attribute and body on one line: the two methods must sit on adjacent source lines.
        [MethodImpl(MethodImplOptions.NoInlining)] public int First() { return 4101; }
        [MethodImpl(MethodImplOptions.NoInlining)] public int Second() { return 4102; }
    }
}
