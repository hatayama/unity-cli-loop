// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class BranchingMethodFixture
    {
        public string Classify(int value)
        {
            // This comment line has no sequence point; resolving it rounds forward.
            if (value < 0)
            {
                return "negative";
            }

            if (value == 0)
            {
                return "zero";
            }

            return "positive";
        }
    }
}
