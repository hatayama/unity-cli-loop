using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose same-file caller stays unchanged after an int-to-long return-type
    /// change because of an implicit widening conversion.
    /// </summary>
    public class HotReloadSignatureChangeUnchangedCallerFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public long StoreTarget(int value)
        {
            return Target(value);
        }
    }
}
