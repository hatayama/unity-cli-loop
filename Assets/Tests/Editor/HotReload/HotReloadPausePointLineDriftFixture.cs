using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host for the FB9 line-drift repro: three consecutive methods so that, after inserting
    /// three lines at the file top, reading the unpatched method's edited line as a compiled
    /// line number would land on AfterTarget.
    /// </summary>
    public class HotReloadPausePointLineDriftFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int PatchTarget()
        {
            return 11;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int UnpatchedTarget()
        {
            return 22;
        }
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int AfterTarget()
        {
            return 33;
        }
    }
}
