using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal MonoBehaviour for the PhysicsCallbackExistingInstance regression harness.
    // OnCollisionEnter2D holds the direct pause-point marker line: a Harmony patch applied to this
    // method after the GameObject already exists in the scene has been observed in real projects
    // to miss Unity's physics message dispatch for that instance. The trigger condition is
    // environment-dependent and does not reproduce deterministically (see docs/regression-harness.md
    // and SourcePausePointConstants.PhysicalCallbackMayMissExistingInstanceWarning).
    public sealed class PhysicsCallbackFloor : MonoBehaviour
    {
        public int HitCount { get; private set; }

        public int IndirectHitCount { get; private set; }

        private void OnCollisionEnter2D(Collision2D collision)
        {
            HitCount++;
            RecordIndirectHit();
        }

        // One-hop indirect callee: the dominant miss pattern reported from real games places the
        // pause-point marker in a small method called from the physics callback rather than in the
        // callback itself, so the harness probes this shape as a separate scenario.
        private void RecordIndirectHit()
        {
            IndirectHitCount++;
        }
    }
}
