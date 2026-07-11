// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointPatcherTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal ref struct PatcherRefStructInstanceMethodFixture
    {
        public int Value;

        public int Double()
        {
            int doubled = Value * 2;
            return doubled;
        }
    }
}
