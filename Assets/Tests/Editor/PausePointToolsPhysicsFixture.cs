// FROZEN FIXTURE: content and line numbers are asserted by PausePointTests.
// Do not reformat or edit this file; add a new fixture file instead.
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures
{
    internal sealed class EnableBySourceLocationPhysicsFixture : MonoBehaviour
    {
        private void OnCollisionEnter2D(Collision2D collision)
        {
            int unused = 1;
        }
    }
}
