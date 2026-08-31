#nullable enable
using System;
using UnityEngine;
using UnityEngine.EventSystems;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class DemoVirtualPad : MonoBehaviour, IPointerDownHandler, IDragHandler, IPointerUpHandler
    {
        [SerializeField] private RectTransform knob = null!;
        [SerializeField] private float padRadius = 80f;

        private RectTransform rectTransform = null!;

        public event Action<Vector2>? OnDirectionChanged;

        public Vector2 Direction { get; private set; }

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            Debug.Assert(knob != null, "knob must be assigned in Inspector");
        }

        public void OnPointerDown(PointerEventData eventData)
        {
            UpdateKnobPosition(eventData);
        }

        public void OnDrag(PointerEventData eventData)
        {
            UpdateKnobPosition(eventData);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            knob.anchoredPosition = Vector2.zero;
            Direction = Vector2.zero;
            OnDirectionChanged?.Invoke(Direction);
            Debug.Log("[Demo] VirtualPad released");
        }

        private void UpdateKnobPosition(PointerEventData eventData)
        {
            // eventData.delta / scaleFactor is unreliable when Game view scale != 1.0
            RectTransformUtility.ScreenPointToLocalPointInRectangle(
                rectTransform, eventData.position, eventData.pressEventCamera, out Vector2 localPoint);

            Vector2 clampedPosition = Vector2.ClampMagnitude(localPoint, padRadius);
            knob.anchoredPosition = clampedPosition;

            Direction = clampedPosition / padRadius;
            OnDirectionChanged?.Invoke(Direction);

            Debug.Log($"[Demo] VirtualPad direction: {Direction}");
        }
    }
}
