// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointResolverTests.
// Do not reformat or edit this file; add a new fixture file instead.
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures
{
    internal sealed class MultiLineAssertFixture
    {
        public void Check(int value)
        {
            Debug.Assert(
                value > 0,
                "value must be positive");
        }
    }
}
