using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP
{
    public class RecordInputOverlayView : MonoBehaviour
    {
        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private GameObject _countdownGroup;
        [SerializeField] private Text _countdownText;
        [SerializeField] private GameObject _recordingGroup;
        [SerializeField] private Text _recDotText;
        [SerializeField] private Text _statusText;

        private void Awake()
        {
            Debug.Assert(_canvasGroup != null, "_canvasGroup must be assigned in prefab");
            Debug.Assert(_countdownGroup != null, "_countdownGroup must be assigned in prefab");
            Debug.Assert(_countdownText != null, "_countdownText must be assigned in prefab");
            Debug.Assert(_recordingGroup != null, "_recordingGroup must be assigned in prefab");
            Debug.Assert(_recDotText != null, "_recDotText must be assigned in prefab");
            Debug.Assert(_statusText != null, "_statusText must be assigned in prefab");

            _canvasGroup.interactable = false;
            _canvasGroup.blocksRaycasts = false;
            Hide();
        }

        public void ShowCountdown(int remainingSeconds)
        {
            _countdownGroup.SetActive(true);
            _recordingGroup.SetActive(false);
            _countdownText.text = $"REC in {remainingSeconds}...";
            _canvasGroup.alpha = 1f;
        }

        public void ShowRecording(string statusText, float dotAlpha)
        {
            _countdownGroup.SetActive(false);
            _recordingGroup.SetActive(true);
            _statusText.text = statusText;
            SetDotAlpha(dotAlpha);
            _canvasGroup.alpha = 1f;
        }

        public void ShowStopped()
        {
            _countdownGroup.SetActive(false);
            _recordingGroup.SetActive(true);
            _statusText.text = "REC STOPPED";
            SetDotAlpha(1f);
        }

        private void SetDotAlpha(float alpha)
        {
            Color dotColor = _recDotText.color;
            dotColor.a = alpha;
            _recDotText.color = dotColor;
        }

        public void SetAlpha(float alpha)
        {
            _canvasGroup.alpha = alpha;
        }

        public void Hide()
        {
            _canvasGroup.alpha = 0f;
            _countdownGroup.SetActive(false);
            _recordingGroup.SetActive(false);
        }
    }
}
