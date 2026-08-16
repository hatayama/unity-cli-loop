using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host with a return-type-change target, a same-file caller, an unrelated
    /// method, and a deletable method. External callers live in another file.
    /// </summary>
    public class HotReloadSignatureChangeExternalHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Target(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int SameFileCaller(int value)
        {
            return Target(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Unrelated(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ToDelete(int value)
        {
            return value;
        }
    }
}
