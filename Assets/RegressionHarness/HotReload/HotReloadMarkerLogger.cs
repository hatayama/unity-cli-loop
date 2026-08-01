using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal MonoBehaviour for the HotReload regression harness.
    // Update logs a string literal each frame so the driver can sed that literal inside the
    // method body, hot-reload Update, and assert via get-logs that PlayMode output flips without
    // a domain reload (see docs/regression-harness.md).
    // Why a body literal (not a const or field initializer): hot-reload shims contain only the
    // rewritten method body. A class-level const is resolved from the already-compiled (stale)
    // assembly and C# bakes its value into IL at shim compile time, so sed'ing the const is a
    // silent no-op despite Success=true. Field initializers are likewise outside method bodies.
    public sealed class HotReloadMarkerLogger : MonoBehaviour
    {
        private void Update()
        {
            Debug.Log("[HotReloadHarness] marker=111");
        }
    }
}
