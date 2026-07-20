// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointPatcherTests.
// Do not reformat or edit this file; add a new fixture file instead.
using System.Runtime.CompilerServices;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal static class PatcherAggressiveInliningMethodFixture
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int Classify(int value)
        {
            int result;
            if (value > 100)
            {
                result = value * 2;
            }
            else if (value > 10)
            {
                result = value + 5;
            }
            else
            {
                result = value - 1;
            }

            return result;
        }
    }
}
