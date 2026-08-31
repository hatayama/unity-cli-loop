using UnityEngine;
using UnityEngine.EventSystems;

namespace io.github.hatayama.UnityCliLoop.Tests
{

/// <summary>
/// Test support type used by editor and play mode fixtures.
/// </summary>
public class DraggableItem : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    private RectTransform rectTransform;
    private Canvas canvas;
    private Vector2 startPosition;

    private void Awake()
    {
        rectTransform = GetComponent<RectTransform>();
        canvas = GetComponentInParent<Canvas>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        startPosition = rectTransform.anchoredPosition;
        Debug.Log($"BeginDrag: {gameObject.name} at {startPosition}");
    }

    public void OnDrag(PointerEventData eventData)
    {
        rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        Debug.Log($"EndDrag: {gameObject.name} moved from {startPosition} to {rectTransform.anchoredPosition}");
    }
}
}
