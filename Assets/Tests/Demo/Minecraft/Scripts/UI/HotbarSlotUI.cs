using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class HotbarSlotUI : MonoBehaviour
    {
        [SerializeField] private Image blockColorImage;
        [SerializeField] private Image selectionFrame;
        [SerializeField] private Text blockNameText;

        private static readonly Color SelectedFrameColor = Color.white;
        private static readonly Color DeselectedFrameColor = new Color(0.5f, 0.5f, 0.5f, 0.5f);

        public void SetBlockInfo(string displayName, Color blockColor)
        {
            if (blockColorImage != null)
            {
                blockColorImage.color = blockColor;
            }

            if (blockNameText != null)
            {
                blockNameText.text = displayName;
            }
        }

        public void SetSelected(bool selected)
        {
            if (selectionFrame == null)
            {
                return;
            }

            selectionFrame.color = selected ? SelectedFrameColor : DeselectedFrameColor;
            transform.localScale = selected ? Vector3.one * 1.1f : Vector3.one;
        }
    }
}
