#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop
{
    // Cycles bullet color via mouse scroll wheel and shows a HUD indicator.
    public class DemoWeaponSelector : MonoBehaviour
    {
        private static readonly Color[] BULLET_COLORS =
        {
            Color.yellow,
            Color.red,
            new Color(0.2f, 0.6f, 1f),
            Color.green,
            new Color(1f, 0.5f, 0f),
            Color.magenta,
        };

        private static readonly string[] COLOR_NAMES =
        {
            "Yellow",
            "Red",
            "Blue",
            "Green",
            "Orange",
            "Magenta",
        };

        private int _selectedIndex;
        private float _scrollAccumulator;
        private const float SCROLL_THRESHOLD = 20f;

        private Image? _colorSwatch;
        private Text? _colorLabel;

        public Color SelectedColor => BULLET_COLORS[_selectedIndex];

        private void Start()
        {
            BuildHud();
            UpdateHud();
        }

        private void Update()
        {
            Mouse? mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            float scrollY = mouse.scroll.ReadValue().y;
            if (Mathf.Approximately(scrollY, 0f))
            {
                return;
            }

            _scrollAccumulator += scrollY;

            if (_scrollAccumulator >= SCROLL_THRESHOLD)
            {
                _scrollAccumulator = 0f;
                _selectedIndex = (_selectedIndex + 1) % BULLET_COLORS.Length;
                UpdateHud();
            }
            else if (_scrollAccumulator <= -SCROLL_THRESHOLD)
            {
                _scrollAccumulator = 0f;
                _selectedIndex = (_selectedIndex - 1 + BULLET_COLORS.Length) % BULLET_COLORS.Length;
                UpdateHud();
            }
        }

        private void BuildHud()
        {
            GameObject canvasGo = new GameObject("WeaponSelectorCanvas");
            canvasGo.transform.SetParent(transform, false);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 100;

            CanvasGroup canvasGroup = canvasGo.AddComponent<CanvasGroup>();
            canvasGroup.interactable = false;
            canvasGroup.blocksRaycasts = false;

            GameObject containerGo = new GameObject("Container");
            containerGo.transform.SetParent(canvasGo.transform, false);
            RectTransform containerRect = containerGo.AddComponent<RectTransform>();
            containerRect.anchorMin = new Vector2(1f, 0f);
            containerRect.anchorMax = new Vector2(1f, 0f);
            containerRect.pivot = new Vector2(1f, 0f);
            containerRect.anchoredPosition = new Vector2(-24f, 24f);
            containerRect.sizeDelta = new Vector2(180f, 40f);

            Image containerBg = containerGo.AddComponent<Image>();
            containerBg.color = new Color(0f, 0f, 0f, 0.5f);
            containerBg.raycastTarget = false;

            GameObject swatchGo = new GameObject("ColorSwatch");
            swatchGo.transform.SetParent(containerGo.transform, false);
            RectTransform swatchRect = swatchGo.AddComponent<RectTransform>();
            swatchRect.anchorMin = new Vector2(0f, 0.5f);
            swatchRect.anchorMax = new Vector2(0f, 0.5f);
            swatchRect.pivot = new Vector2(0f, 0.5f);
            swatchRect.anchoredPosition = new Vector2(8f, 0f);
            swatchRect.sizeDelta = new Vector2(24f, 24f);
            _colorSwatch = swatchGo.AddComponent<Image>();
            _colorSwatch.raycastTarget = false;

            GameObject labelGo = new GameObject("ColorLabel");
            labelGo.transform.SetParent(containerGo.transform, false);
            RectTransform labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = new Vector2(0f, 0f);
            labelRect.anchorMax = new Vector2(1f, 1f);
            labelRect.offsetMin = new Vector2(40f, 0f);
            labelRect.offsetMax = new Vector2(-4f, 0f);
            _colorLabel = labelGo.AddComponent<Text>();
            _colorLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            _colorLabel.fontSize = 20;
            _colorLabel.alignment = TextAnchor.MiddleLeft;
            _colorLabel.color = Color.white;
            _colorLabel.raycastTarget = false;
        }

        private void UpdateHud()
        {
            Debug.Assert(_colorSwatch != null, "_colorSwatch must be initialized");
            Debug.Assert(_colorLabel != null, "_colorLabel must be initialized");
            _colorSwatch!.color = BULLET_COLORS[_selectedIndex];
            _colorLabel!.text = COLOR_NAMES[_selectedIndex];
        }
    }
}
#endif
