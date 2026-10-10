using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Compiled host whose one method the caller-note backfill E2E edits; its only compiled
    /// caller sits in the cross-assembly fixture, so the budget of a cold run refuses that dll.
    /// </summary>
    public class HotReloadCallerNoteBackfillE2EFixture
    {
        [MethodImpl(MethodImplOptions.NoInlining)]
        public int Read()
        {
            return 1;
        }
    }
}
