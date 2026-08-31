// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
using System;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class TryFinallyMethodFixture
    {
        public int Divide(int numerator, int denominator)
        {
            int result;
            try
            {
                result = numerator / denominator;
            }
            finally
            {
                Console.WriteLine("Divide attempted.");
            }

            return result;
        }
    }
}
