using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled callers of the cross-assembly stale-signature host from a separate assembly, so
    /// the host's group never holds them as entries.
    /// </summary>
    public class HotReloadCrossAssemblyStaleSignatureCaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallDeleted(int value)
        {
            return new HotReloadCrossAssemblyStaleSignatureHost().ToDelete(value);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallReturnTypeTarget(int value)
        {
            return new HotReloadCrossAssemblyStaleSignatureHost().ReturnTypeTarget(value);
        }
    }
}
