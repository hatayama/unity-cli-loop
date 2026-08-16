using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled caller of TargetGated so that replacement stays uncovered in a multi-change run.
    /// </summary>
    public class HotReloadSignatureChangeMultiReplacementCaller
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CallGated(int value)
        {
            return new HotReloadSignatureChangeMultiReplacementHost().TargetGated(value);
        }
    }
}
