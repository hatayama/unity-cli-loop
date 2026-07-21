using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Trigger-collider counterpart of PhysicsCallbackFloor, added because the reported feedback
    // hit this same "existing instance" gap on both an OnCollision* and an OnTrigger* callback --
    // the two dispatch through separate Unity code paths, so the collision-only repro does not
    // prove the trigger path shares the same fix.
    public sealed class PhysicsCallbackTriggerZone : MonoBehaviour
    {
        public int HitCount { get; private set; }

        private void OnTriggerEnter2D(Collider2D other)
        {
            HitCount++;
        }
    }
}
