#nullable enable
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop
{
    public class SimulateKeyboardOverlay : MonoBehaviour
    {
        public const float CONTAINER_BACKGROUND_ALPHA = 0.8f;

        private const float PRESS_DISPLAY_DURATION = 0.5f;
        private const float FADE_DURATION = 0.2f;
        private const int FONT_SIZE = 48;
        private const int GLYPH_FONT_SIZE = 64;
        private static readonly Color ContainerBackgroundColor = new Color(0.15f, 0.15f, 0.15f, CONTAINER_BACKGROUND_ALPHA);

        [SerializeField] private GameObject _container = null!;
        [SerializeField] private Image _containerImage = null!;

        private readonly List<BadgeEntry> _badgePool = new();
        private readonly List<string> _cachedKeyNames = new();
        private readonly List<string> _displayKeys = new();

        private void Awake()
        {
            Debug.Assert(_container != null, "_container must be assigned in prefab");
            Debug.Assert(_containerImage != null, "_containerImage must be assigned in prefab");

            // Prefab keeps preview badges for editor inspection, but runtime badges must start empty.
            for (int i = _container!.transform.childCount - 1; i >= 0; i--)
            {
                DestroyImmediate(_container.transform.GetChild(i).gameObject);
            }

            _container.SetActive(false);
        }

        private void LateUpdate()
        {
            bool hasHeldKeys = SimulateKeyboardOverlayState.HeldKeys.Count > 0;
            string? pressKey = SimulateKeyboardOverlayState.PressKey;
            bool isPressHeld = SimulateKeyboardOverlayState.IsPressHeld;
            float pressElapsedSinceRelease = 0f;

            if (pressKey != null && !isPressHeld)
            {
                pressElapsedSinceRelease = Time.realtimeSinceStartup - SimulateKeyboardOverlayState.PressReleasedTime;
                if (pressElapsedSinceRelease > PRESS_DISPLAY_DURATION + FADE_DURATION)
                {
                    SimulateKeyboardOverlayState.ClearPress();
                    pressKey = null;
                }
            }

            if (!hasHeldKeys && pressKey == null)
            {
                SetBadgeCount(0);
                _container.SetActive(false);
                return;
            }

            _displayKeys.Clear();
            IReadOnlyList<string> heldKeys = SimulateKeyboardOverlayState.HeldKeys;
            for (int i = 0; i < heldKeys.Count; i++)
            {
                _displayKeys.Add(heldKeys[i]);
            }

            if (pressKey != null && !_displayKeys.Contains(pressKey))
            {
                _displayKeys.Add(pressKey);
            }

            SetBadgeCount(_displayKeys.Count);
            _container.SetActive(true);

            float maxAlpha = 0f;
            for (int i = 0; i < _displayKeys.Count; i++)
            {
                float alpha = GetBadgeAlpha(_displayKeys[i], pressKey, isPressHeld, pressElapsedSinceRelease);
                UpdateBadge(_badgePool[i], i, _displayKeys[i], alpha);
                if (alpha > maxAlpha)
                {
                    maxAlpha = alpha;
                }
            }

            _containerImage.color = new Color(
                ContainerBackgroundColor.r,
                ContainerBackgroundColor.g,
                ContainerBackgroundColor.b,
                ContainerBackgroundColor.a * maxAlpha);
        }

        private void SetBadgeCount(int count)
        {
            while (_badgePool.Count < count)
            {
                _badgePool.Add(CreateBadge());
                _cachedKeyNames.Add("");
            }

            for (int i = 0; i < _badgePool.Count; i++)
            {
                _badgePool[i].Root.SetActive(i < count);
            }
        }

        private BadgeEntry CreateBadge()
        {
            GameObject badge = new GameObject("KeyBadge");
            badge.transform.SetParent(_container.transform, false);

            badge.AddComponent<RectTransform>();

            Image bg = badge.AddComponent<Image>();
            bg.color = new Color(0f, 0f, 0f, 0f);
            bg.raycastTarget = false;

            LayoutElement layoutElement = badge.AddComponent<LayoutElement>();

            GameObject textGo = new GameObject("KeyText");
            textGo.transform.SetParent(badge.transform, false);

            RectTransform textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text text = textGo.AddComponent<Text>();
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = FONT_SIZE;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.raycastTarget = false;

            // Thicken glyph strokes so Unicode symbols don't look too thin
            Outline outline = textGo.AddComponent<Outline>();
            outline.effectColor = Color.white;
            outline.effectDistance = new Vector2(1f, -1f);

            return new BadgeEntry(badge, bg, text, layoutElement);
        }

        private void UpdateBadge(BadgeEntry badge, int index, string keyName, float alpha)
        {
            badge.Text.color = new Color(1f, 1f, 1f, alpha);

            if (_cachedKeyNames[index] == keyName)
            {
                return;
            }

            _cachedKeyNames[index] = keyName;
            string symbol = KeySymbolMap.GetSymbol(keyName);
            badge.Text.text = symbol;
            badge.Text.fontSize = KeySymbolMap.IsGlyphSymbol(symbol) ? GLYPH_FONT_SIZE : FONT_SIZE;

            TextGenerationSettings settings = badge.Text.GetGenerationSettings(new Vector2(float.MaxValue, float.MaxValue));
            float preferredWidth = badge.Text.cachedTextGeneratorForLayout.GetPreferredWidth(symbol, settings);
            float preferredHeight = badge.Text.cachedTextGeneratorForLayout.GetPreferredHeight(symbol, settings);

            badge.Layout.preferredWidth = Mathf.Max(preferredWidth / badge.Text.pixelsPerUnit, preferredHeight / badge.Text.pixelsPerUnit);
            badge.Layout.preferredHeight = preferredHeight / badge.Text.pixelsPerUnit;
        }

        private static float GetBadgeAlpha(
            string keyName,
            string? pressKey,
            bool isPressHeld,
            float pressElapsedSinceRelease)
        {
            if (pressKey == null || keyName != pressKey || isPressHeld || pressElapsedSinceRelease <= PRESS_DISPLAY_DURATION)
            {
                return 1f;
            }

            float fadeT = Mathf.Clamp01((pressElapsedSinceRelease - PRESS_DISPLAY_DURATION) / FADE_DURATION);
            return 1f - fadeT;
        }

        private readonly struct BadgeEntry
        {
            public readonly GameObject Root;
            public readonly Image Background;
            public readonly Text Text;
            public readonly LayoutElement Layout;

            public BadgeEntry(GameObject root, Image background, Text text, LayoutElement layout)
            {
                Root = root;
                Background = background;
                Text = text;
                Layout = layout;
            }
        }
    }
}
