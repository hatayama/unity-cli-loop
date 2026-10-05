// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointClosureFrameCaptureTests.
// Do not reformat or edit this file; add a new fixture file instead.
using System;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal sealed class PatcherClosureFrameLocalFunctionFixture
    {
        private readonly int _value;

        public PatcherClosureFrameLocalFunctionFixture(int value)
        {
            _value = value;
        }

        public int SumInLoopClosure(int delta)
        {
            int total = 0;
            for (int j = 0; j < 3; j++)
            {
                Func<int> read = () => j;
                int Local() => _value + delta + j;
                total += Local() + read();
            }

            return total;
        }

        public int SumWithoutClosureClass(int delta)
        {
            int offset = delta * 2;
            return Local();

            int Local()
            {
                int sum = _value + offset;
                return sum;
            }
        }

        public static int SumInStaticMethod(int delta)
        {
            int offset = delta * 2;
            return Local();

            int Local()
            {
                int sum = offset + 1;
                return sum;
            }
        }

        public static int SumAcrossNestedScopes(int delta)
        {
            int offset = delta * 2;
            if (delta > 0)
            {
                int inner = delta + 1;
                return Local();

                int Local()
                {
                    int sum = offset + inner;
                    return sum;
                }
            }

            return 0;
        }

        public int SumWithParameterAcrossNestedScopes(int delta)
        {
            int offset = delta * 2;
            if (delta > 0)
            {
                int inner = delta + 1;
                return Local(100);

                int Local(int extra)
                {
                    int sum = _value + offset + inner + extra;
                    return sum;
                }
            }

            return 0;
        }
    }
}
