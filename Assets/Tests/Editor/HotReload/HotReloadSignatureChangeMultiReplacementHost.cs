using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host with one replacement that has an external caller and one that is
    /// only called from this file, so a single edit can gate one change and apply the other.
    /// </summary>
    public class HotReloadSignatureChangeMultiReplacementHost
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int TargetGated(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int TargetCovered(int value)
        {
            return value;
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int CoveredCaller(int value)
        {
            return TargetCovered(value);
        }
    }
}
