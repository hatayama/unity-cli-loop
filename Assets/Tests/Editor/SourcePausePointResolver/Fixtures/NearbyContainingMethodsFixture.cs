// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class NearbyContainingMethodsFixture
    {
        public int Host(int value)
        {
            System.Func<int, int> first = x => x + 1; System.Func<int, int> second = y => y + 2; System.Func<int, int> third = z => z + 3; return first(value) + second(value) + third(value);
        }
    }
}
