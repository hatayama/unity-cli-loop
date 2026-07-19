// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointPatcherTests.
// Do not reformat or edit this file; add a new fixture file instead.
namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal sealed class PatcherPhysicsNamedMethodOnPlainClassFixture
    {
        public int HitCount;

        public void OnTriggerEnter2D()
        {
            HitCount++;
        }
    }
}
