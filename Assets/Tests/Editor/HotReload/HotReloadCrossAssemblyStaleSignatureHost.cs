using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host with a deletable method, a return-type-change target, and an unrelated
    /// method. Its only compiled callers live in another assembly, so a signature change here
    /// has callers outside the host's group.
    /// </summary>
    public class HotReloadCrossAssemblyStaleSignatureHost
    {
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

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReturnTypeTarget(int value)
        {
            return value;
        }
    }
}
