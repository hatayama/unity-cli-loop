using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal uGUI demo for manual hot-reload checks: orbits the attached Image around the
    // screen center. The orbit radius (pixels) and speed (degrees per second) are literals inside
    // Update on purpose so they can be tuned with `uloop hot-reload --files <this file>` while
    // PlayMode is running.
    // Why body literals (not a const, field, or SerializeField): hot-reload shims contain only the
    // rewritten method body, so anything outside the body is read from the stale compiled
    // assembly and a hot-reload of it would be a silent no-op.
    public sealed class SpinningImageRotator : MonoBehaviour
    {
        private RectTransform _rectTransform;
        private float _angleDegrees;

        private void Awake()
        {
            _rectTransform = GetComponent<RectTransform>();
            Debug.Assert(_rectTransform != null, "SpinningImageRotator requires a RectTransform.");
        }

        private void Update()
        {
            float radius = 300f;
            float degreesPerSecond = 90f;

            _angleDegrees += degreesPerSecond * Time.deltaTime;
            float radians = _angleDegrees * Mathf.Deg2Rad;
            _rectTransform.anchoredPosition = new Vector2(Mathf.Cos(radians), Mathf.Sin(radians)) * radius;
            _rectTransform.localRotation = Quaternion.Euler(0f, 0f, _angleDegrees);
        }
    }
}
