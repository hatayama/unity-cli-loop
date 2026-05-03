#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop
{
    public class ClickCounterButton : MonoBehaviour
    {
        [SerializeField] private Text? counterText;
        [SerializeField] private Color toggleColor = Color.yellow;

        private Image image = null!;
        private Color normalColor;
        private bool isToggled;
        private int clickCount;

        // Shared across all ClickCounterButton instances in the scene
        private static int totalClickCount;

        private Button button = null!;

        private void Awake()
        {
            image = GetComponent<Image>();
            normalColor = image.color;
            button = GetComponent<Button>();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticState()
        {
            totalClickCount = 0;
        }

        // Runtime listener registration (AddListener in scene generation is not serialized)
        private void OnEnable()
        {
            button.onClick.AddListener(OnClick);
        }

        private void OnDisable()
        {
            button.onClick.RemoveListener(OnClick);
        }

        public void OnClick()
        {
            clickCount++;
            totalClickCount++;

            isToggled = !isToggled;
            image.color = isToggled ? toggleColor : normalColor;

            if (counterText != null)
            {
                counterText.text = $"Total Clicks: {totalClickCount}";
            }

            Debug.Log($"[Demo] Clicked '{gameObject.name}' (count: {clickCount}, total: {totalClickCount})");
        }
    }
}
