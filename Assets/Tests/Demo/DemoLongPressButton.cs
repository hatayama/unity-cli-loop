#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class DemoLongPressButton : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
    {
        [SerializeField] private float requiredHoldTime = 3f;
        [SerializeField] private Color activatedColor = new Color(1f, 0.4f, 0f, 1f);

        private Image image = null!;
        private Color normalColor;
        private Text label = null!;
        private float pressStartTime;
        private bool isPressed;
        private bool isActivated;

        private void Awake()
        {
            image = GetComponent<Image>();
            normalColor = image.color;
            label = GetComponentInChildren<Text>();
        }

        private void Update()
        {
            if (!isPressed)
            {
                return;
            }

            float elapsed = Time.realtimeSinceStartup - pressStartTime;
            float progress = Mathf.Clamp01(elapsed / requiredHoldTime);

            Color fromColor = isActivated ? activatedColor : normalColor;
            Color toColor = isActivated ? normalColor : activatedColor;
            image.color = Color.Lerp(fromColor, toColor, progress);
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            pressStartTime = Time.realtimeSinceStartup;
            isPressed = true;
            Debug.Log("[Demo] LongPressButton: pointer down");
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!isPressed)
            {
                return;
            }

            float heldDuration = Time.realtimeSinceStartup - pressStartTime;
            isPressed = false;

            if (heldDuration >= requiredHoldTime)
            {
                isActivated = !isActivated;
                image.color = isActivated ? activatedColor : normalColor;

                if (label != null)
                {
                    label.text = isActivated ? "Activated!" : $"Hold {requiredHoldTime:F0}s";
                }

                Debug.Log($"[Demo] LongPressButton: toggled (held {heldDuration:F1}s)");
            }
            else
            {
                // Snap back to current state color
                image.color = isActivated ? activatedColor : normalColor;
                Debug.Log($"[Demo] LongPressButton: released too early ({heldDuration:F1}s < {requiredHoldTime}s)");
            }
        }
    }
}
