using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal uGUI demo for manual hot-reload checks: rotates the attached Image around the
    // screen center every frame. Edit the literal degrees-per-second inside Update (or flip the
    // sign) and run `uloop hot-reload --files <this file>` to confirm the spin changes without a
    // domain reload.
    // Why a body literal (not a const, field, or SerializeField): hot-reload shims contain only the
    // rewritten method body, so anything outside the body is read from the stale compiled
    // assembly and a hot-reload of it would be a silent no-op.
    public sealed class SpinningImageRotator : MonoBehaviour
    {
        private void Update()
        {
            transform.Rotate(0f, 0f, 90f * Time.deltaTime);
        }
    }
}
