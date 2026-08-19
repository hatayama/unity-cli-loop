// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class CompiledMethodSpanFixture
    {
        public int Target(int value)
        {
            int doubled = value * 2;
            return doubled;
        }

        public int OtherMethod(int value)
        {
            int tripled = value * 3;
            return tripled;
        }
    }
}
