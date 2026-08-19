using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host used to pin that deleting a helper which called Target does not gate a
    /// same-file return-type change of Target.
    /// </summary>
    public class HotReloadSignatureChangeHelperDeleteFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Helper(int value)
        {
            return Target(value);
        }
    }
}
