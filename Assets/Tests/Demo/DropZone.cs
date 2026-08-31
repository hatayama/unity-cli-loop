#nullable enable
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class DropZone : MonoBehaviour, IDropHandler
    {
        [SerializeField] private Text? statusText;
        [SerializeField] private Color highlightColor = new Color(0.5f, 1f, 0.5f, 1f);

        public string StatusMessage => statusText != null ? statusText.text : "";

        private Image image = null!;
        private Color normalColor;

        private void Awake()
        {
            image = GetComponent<Image>();
            normalColor = image.color;
        }

        // UIElementAnnotator detects IDropHandler to list this as a "DropTarget" in annotated screenshots
        public void OnDrop(PointerEventData eventData)
        {
            GameObject? dragged = eventData.pointerDrag;
            if (dragged != null)
            {
                OnItemDropped(dragged);
            }
        }

        public void OnItemDropped(GameObject item)
        {
            if (statusText != null)
            {
                statusText.text = $"Dropped: {item.name}";
            }

            Debug.Log($"[Demo] '{item.name}' dropped on DropZone");
            StopAllCoroutines();
            StartCoroutine(FlashHighlight());
        }

        private IEnumerator FlashHighlight()
        {
            image.color = highlightColor;
            yield return new WaitForSeconds(0.3f);
            image.color = normalColor;
        }
    }
}
