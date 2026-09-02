// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointPostLineCaptureTests.
// Do not reformat or edit this file; add a new fixture file instead.
using System;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal static class PatcherPostLineFixture
    {
        public static int Double(int value)
        {
            int doubled = 0;
            doubled = value * 2;
            return doubled + 1;
        }

        public static int SquareUnlessNegative(int value)
        {
            if (value < 0)
            {
                return -1;
            }

            int squared = value * value;
            return squared;
        }

        public static int AlwaysThrow(int value)
        {
            throw new InvalidOperationException("always throws: " + value);
        }
    }
}
