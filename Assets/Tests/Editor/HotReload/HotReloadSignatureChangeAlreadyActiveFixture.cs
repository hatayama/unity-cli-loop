using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host for the already-active gate sequence: a same-file return-type
    /// change plus a property that a later run can rewrite to skip the caller.
    /// </summary>
    public class HotReloadSignatureChangeAlreadyActiveFixture
    {
        public int MarkerHp { get; set; }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Unrelated(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ExistingCaller(int value)
        {
            return Target(value);
        }
    }
}
