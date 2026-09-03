using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host with two Target overloads so a return-type change on one must not
    /// record the other as superseded.
    /// </summary>
    public class HotReloadSignatureChangeOverloadFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(long value)
        {
            return (int)value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingCaller(int value)
        {
            return Target(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int LongCaller(long value)
        {
            return Target(value);
        }
    }
}
