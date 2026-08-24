using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host for file-level all-or-nothing apply. InitField and ReadField are
    /// edited together so a compile failure in InitField must skip ReadField.
    /// </summary>
    public class HotReloadAtomicFileApplyFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public void InitField()
        {
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        public int ReadField()
        {
            return 0;
        }
    }
}
