// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointPatcherTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal sealed class PatcherNormalMethodFixture
    {
        public string Tag = "fixture-instance";

        public int Add(int left, int right)
        {
            int sum = left + right;
            return sum;
        }
    }
}
