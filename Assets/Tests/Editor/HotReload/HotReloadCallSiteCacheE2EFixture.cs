using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose one method the call-site cache E2E edits in two runs. Kept apart from
    /// the other fixtures so their patches cannot change what these runs patch and scan.
    /// </summary>
    public class HotReloadCallSiteCacheE2EFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read()
        {
            return 1;
        }
    }
}
