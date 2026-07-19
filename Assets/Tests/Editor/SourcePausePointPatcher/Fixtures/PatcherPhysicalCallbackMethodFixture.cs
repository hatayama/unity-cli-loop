// FROZEN FIXTURE: content and line numbers are asserted by SourcePausePointPatcherTests.
// Do not reformat or edit this file; add a new fixture file instead.
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.SourcePausePointPatcherFixtures
{
    internal sealed class PatcherPhysicalCallbackMethodFixture : MonoBehaviour
    {
        public int HitCount;

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HitCount++;
        }

        private void Update()
        {
            HitCount++;
        }
    }
}
