#nullable enable
using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP
{
    // Overlay that displays replay progress (frame counter and progress bar)
    // during input replay. Keyboard/mouse visualization is handled by existing
    // SimulateKeyboardOverlay and SimulateMouseInputOverlay.
    public class ReplayInputOverlay : MonoBehaviour
    {
        private const float FADE_OUT_DURATION = 0.5f;

        [SerializeField] private Text? _statusText;
        [SerializeField] private Image? _progressBarFill;
        [SerializeField] private GameObject? _loopIcon;

        private CanvasGroup _canvasGroup = null!;
        private bool _wasActive;
        private float _deactivatedTime;

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
        }

        private void LateUpdate()
        {
            if (ReplayInputOverlayState.IsActive)
            {
                _wasActive = true;
                _deactivatedTime = 0f;
                SetVisible(true);
                UpdateDisplay();
                return;
            }

            if (!_wasActive)
            {
                return;
            }

            float elapsed = Time.realtimeSinceStartup - _deactivatedTime;
            if (_deactivatedTime <= 0f)
            {
                _deactivatedTime = Time.realtimeSinceStartup;
                UpdateStatusText("REPLAY COMPLETE");
                return;
            }

            if (elapsed > FADE_OUT_DURATION)
            {
                SetVisible(false);
                _wasActive = false;
                _deactivatedTime = 0f;
                return;
            }

            float alpha = 1f - (elapsed / FADE_OUT_DURATION);
            _canvasGroup.alpha = alpha;
        }

        private void UpdateDisplay()
        {
            int currentFrame = ReplayInputOverlayState.CurrentFrame;
            int totalFrames = ReplayInputOverlayState.TotalFrames;
            bool isLooping = ReplayInputOverlayState.IsLooping;

            string loopLabel = isLooping ? " \u21BB" : "";
            UpdateStatusText($"REPLAY {currentFrame} / {totalFrames}{loopLabel}");

            if (_progressBarFill != null)
            {
                _progressBarFill.fillAmount = ReplayInputOverlayState.Progress;
            }

            if (_loopIcon != null)
            {
                _loopIcon.SetActive(isLooping);
            }
        }

        private void UpdateStatusText(string text)
        {
            if (_statusText != null)
            {
                _statusText.text = text;
            }
        }

        private void SetVisible(bool visible)
        {
            _canvasGroup.alpha = visible ? 1f : 0f;
        }
    }
}
