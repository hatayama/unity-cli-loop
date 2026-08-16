using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose Target return-type change is gated by an unchanged same-file caller.
    /// </summary>
    public class HotReloadSignatureChangeSameNameGatedHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public long Store(int value)
        {
            return Target(value);
        }
    }

    /// <summary>
    /// Same-file sibling type that also declares Target, so a deletion of this method
    /// must not be suppressed when the gated replacement shares the simple name.
    /// </summary>
    public class HotReloadSignatureChangeSameNameDeletedHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }
    }
}
