#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop
{
    // Overlay that renders a mouse device icon on Game View and highlights
    // pressed buttons / scroll direction during simulate-mouse-input tool calls.
    public class SimulateMouseInputOverlay : MonoBehaviour
    {
        private const float DISPLAY_DURATION = 1.0f;

        private static readonly Color BUTTON_PRESSED_COLOR = new Color(1f, 1f, 1f, 0.95f);

        [SerializeField] private Image? _leftButton;
        [SerializeField] private Image? _rightButton;
        [SerializeField] private Image? _scrollWheel;
        [SerializeField] private Image? _scrollArrowTop;
        [SerializeField] private Image? _scrollArrowBottom;
        [SerializeField] private RectTransform? _moveDirectionGroup;

        private CanvasGroup _canvasGroup = null!;
        private bool _isVisible;

        private Color _leftButtonIdleColor;
        private Color _rightButtonIdleColor;
        private Color _scrollWheelIdleColor;

        private void Awake()
        {
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;

            SetVisible(false);
            CaptureIdleColors();
        }

        private void LateUpdate()
        {
            if (SimulateMouseInputOverlayState.HasAnyActivity)
            {
                if (!_isVisible)
                {
                    SetVisible(true);
                }

                UpdateButtonColors();
                UpdateScrollIndicator();
                UpdateMoveDirection();
                return;
            }

            float elapsed = Time.realtimeSinceStartup - SimulateMouseInputOverlayState.LastActivityTime;

            if (SimulateMouseInputOverlayState.LastActivityTime <= 0f || elapsed > DISPLAY_DURATION)
            {
                if (_isVisible)
                {
                    SetVisible(false);
                }

                return;
            }

            UpdateButtonColors();
            UpdateScrollIndicator();
            UpdateMoveDirection();
        }

        private void SetVisible(bool visible)
        {
            _isVisible = visible;
            _canvasGroup.alpha = visible ? 1f : 0f;
        }

        private void CaptureIdleColors()
        {
            Debug.Assert(_leftButton != null, "_leftButton must be assigned before CaptureIdleColors");
            Debug.Assert(_rightButton != null, "_rightButton must be assigned before CaptureIdleColors");
            Debug.Assert(_scrollWheel != null, "_scrollWheel must be assigned before CaptureIdleColors");

            _leftButtonIdleColor = _leftButton!.color;
            _rightButtonIdleColor = _rightButton!.color;
            _scrollWheelIdleColor = _scrollWheel!.color;
        }

        private void UpdateButtonColors()
        {
            _leftButton!.color = SimulateMouseInputOverlayState.IsLeftButtonHeld
                ? BUTTON_PRESSED_COLOR
                : _leftButtonIdleColor;

            _rightButton!.color = SimulateMouseInputOverlayState.IsRightButtonHeld
                ? BUTTON_PRESSED_COLOR
                : _rightButtonIdleColor;

            bool wheelActive = SimulateMouseInputOverlayState.IsMiddleButtonHeld
                               || SimulateMouseInputOverlayState.ScrollDirection != 0;
            _scrollWheel!.color = wheelActive ? BUTTON_PRESSED_COLOR : _scrollWheelIdleColor;
        }

        private void UpdateScrollIndicator()
        {
            int dir = SimulateMouseInputOverlayState.ScrollDirection;

            if (dir == 0)
            {
                _scrollArrowTop!.enabled = false;
                _scrollArrowBottom!.enabled = false;
                return;
            }

            _scrollArrowTop!.enabled = true;
            _scrollArrowBottom!.enabled = true;

            float rotation = dir > 0 ? 0f : 180f;
            _scrollArrowTop.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
            _scrollArrowBottom.rectTransform.localRotation = Quaternion.Euler(0f, 0f, rotation);
        }

        private void UpdateMoveDirection()
        {
            if (_moveDirectionGroup == null)
            {
                return;
            }

            Vector2 delta = SimulateMouseInputOverlayState.MoveDelta;

            if (delta == Vector2.zero)
            {
                _moveDirectionGroup.gameObject.SetActive(false);
                return;
            }

            _moveDirectionGroup.gameObject.SetActive(true);
            float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
            _moveDirectionGroup.localRotation = Quaternion.Euler(0f, 0f, angle);
        }
    }
}