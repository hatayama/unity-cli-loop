using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal MonoBehaviour for the PhysicsCallbackExistingInstance regression harness.
    // OnCollisionEnter2D is the pause-point marker line: a Harmony patch applied to this method
    // after the GameObject already exists in the scene does not intercept Unity's cached physics
    // message dispatch for that instance (see docs/regression-harness.md and
    // SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning).
    public sealed class PhysicsCallbackFloor : MonoBehaviour
    {
        public int HitCount { get; private set; }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HitCount++;
        }
    }
}
