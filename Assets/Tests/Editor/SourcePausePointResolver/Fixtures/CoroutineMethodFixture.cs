// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
using System.Collections;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class CoroutineMethodFixture
    {
        public IEnumerator CountUp(int start)
        {
            int current = start;
            yield return current;
            current = current + 1;
            yield return current;
        }
    }
}
