using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal MonoBehaviour for the HotReloadWiredValuePersistence regression harness.
    // The driver hot-reloads an added Transform field into this file, wires the scene's Target
    // into it from Edit mode, and asserts that Awake and Update of the instance the Play mode
    // scene reload rebuilds read the wired value back.
    // Why compiled Awake and Update: an added Awake is never called by the engine, and the
    // harness has to see the read made while the scene loads as well as a per-frame read.
    public sealed class HotReloadWiredValueHost : MonoBehaviour
    {
        private void Awake()
        {
            Debug.Log("[HotReloadWiredValueHarness] awake-baseline");
        }

        private void Update()
        {
            Debug.Log("[HotReloadWiredValueHarness] update-baseline");
        }
    }
}
