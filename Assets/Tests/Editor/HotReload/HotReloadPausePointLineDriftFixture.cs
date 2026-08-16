using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host for the FB9 line-drift repro: three consecutive methods so inserting
    /// three lines at the file top maps the unpatched method's edited line onto AfterTarget
    /// in the compiled line map.
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
