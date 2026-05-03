#nullable enable
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop
{
    public class DemoDraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        private RectTransform rectTransform = null!;
        private Canvas canvas = null!;
        private CanvasGroup canvasGroup = null!;
        private Vector2 startPosition;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            canvas = GetComponentInParent<Canvas>();
            canvasGroup = GetComponent<CanvasGroup>();
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            startPosition = rectTransform.anchoredPosition;
            canvasGroup.alpha = 0.6f;
            // Let raycasts pass through to the DropZone underneath during drag
            canvasGroup.blocksRaycasts = false;
            rectTransform.localScale = Vector3.one * 1.1f;

            Debug.Log($"[Demo] BeginDrag: {gameObject.name} at {startPosition}");
        }

        public void OnDrag(PointerEventData eventData)
        {
            rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            canvasGroup.blocksRaycasts = true;
            canvasGroup.alpha = 1f;
            rectTransform.localScale = Vector3.one;

            Debug.Log($"[Demo] EndDrag: {gameObject.name} moved from {startPosition} to {rectTransform.anchoredPosition}");
        }
    }
}
