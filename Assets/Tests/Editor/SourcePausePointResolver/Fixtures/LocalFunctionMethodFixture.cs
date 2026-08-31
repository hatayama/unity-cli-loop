// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class LocalFunctionMethodFixture
    {
        public int Square(int value)
        {
            return Compute(value);

            static int Compute(int x)
            {
                int squared = x * x;
                return squared;
            }
        }
    }
}
