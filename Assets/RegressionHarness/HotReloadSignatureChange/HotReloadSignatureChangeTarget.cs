using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal MonoBehaviour for the HotReloadSignatureChange regression harness.
    // SameFileValue is only called from this file; SharedValue is also called from
    // HotReloadSignatureChangeExternalCaller in another compiled file.
    // Why existing Update (not a newly added Unity message): added messages are never
    // discovered on the compiled type, so a new Update would not run.
    public sealed class HotReloadSignatureChangeTarget : MonoBehaviour
    {
        public int SameFileValue()
        {
            return 1;
        }

        public int SharedValue()
        {
            return 2;
        }

        public int ReadShared()
        {
            return SharedValue();
        }

        private void Update()
        {
            Debug.Log("[HotReloadSignatureChangeHarness] same=" + SameFileValue() + ";baseline");
        }
    }
}
