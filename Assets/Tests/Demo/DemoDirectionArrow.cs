#nullable enable
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop
{
    public class DemoDirectionArrow : MonoBehaviour
    {
        [SerializeField] private DemoVirtualPad virtualPad = null!;

        private RectTransform rectTransform = null!;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            Debug.Assert(virtualPad != null, "virtualPad must be assigned in Inspector");
        }

        private void OnEnable()
        {
            if (virtualPad == null)
            {
                return;
            }

            virtualPad.OnDirectionChanged += HandleDirectionChanged;
        }

        private void OnDisable()
        {
            if (virtualPad == null)
            {
                return;
            }

            virtualPad.OnDirectionChanged -= HandleDirectionChanged;
        }

        private void HandleDirectionChanged(Vector2 direction)
        {
            // Near-zero vectors produce unstable Atan2 angles
            if (direction.sqrMagnitude < 0.01f)
            {
                return;
            }

            float angle = Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg;
            rectTransform.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}
