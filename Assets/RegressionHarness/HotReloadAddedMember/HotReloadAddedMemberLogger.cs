using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal MonoBehaviour for the HotReloadAddedMember regression harness.
    // Update logs a baseline marker so the driver can add a field and a method in this
    // same file, rewrite existing ReadAdded/WriteAdded plus Update to use them, and
    // assert PlayMode output plus store persistence without a domain reload.
    // Why existing Update (not a newly added Unity message): added messages are never
    // discovered on the compiled type, so a new Update would not run. Why compiled
    // ReadAdded/WriteAdded: execute-dynamic-code can only call methods that already
    // exist; Harmony then forwards those patched bodies to the side table.
    public sealed class HotReloadAddedMemberLogger : MonoBehaviour
    {
        public int ReadAdded()
        {
            return 0;
        }

        public void WriteAdded(int value)
        {
        }

        private void Update()
        {
            Debug.Log("[HotReloadAddedMemberHarness] baseline");
        }
    }
}
