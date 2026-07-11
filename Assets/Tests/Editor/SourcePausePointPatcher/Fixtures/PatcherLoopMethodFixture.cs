// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointPatcherTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal static class PatcherLoopMethodFixture
    {
        public static int SumUpTo(int count)
        {
            int total = 0;
            for (int i = 0; i < count; i++)
            {
                total += i;
            }
            return total;
        }
    }
}
