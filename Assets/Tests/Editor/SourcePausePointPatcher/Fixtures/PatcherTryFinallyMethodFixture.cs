// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointPatcherTests.
// Do not reformat or edit this file; add a new fixture file instead.
using System;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal static class PatcherTryFinallyMethodFixture
    {
        public static int Divide(int numerator, int denominator)
        {
            int result;
            try
            {
                result = numerator / denominator;
            }
            finally
            {
                GC.KeepAlive(denominator);
            }

            return result;
        }
    }
}
