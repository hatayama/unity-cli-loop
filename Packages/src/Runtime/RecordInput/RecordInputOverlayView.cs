using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides the Unity component behavior for Record Input Overlay View.
    /// </summary>
    public class RecordInputOverlayView : MonoBehaviour
    {
        private const string CountdownGroupName = "CountdownGroup";
        private const string CountdownTextName = "CountdownText";
        private const string RecordingGroupName = "RecordingGroup";
        private const string RecDotName = "RecDot";
        private const string StatusTextName = "StatusText";

        [SerializeField] private CanvasGroup _canvasGroup;
        [SerializeField] private GameObject _countdownGroup;
        [SerializeField] private Text _countdownText;
        [SerializeField] private GameObject _recordingGroup;
        [SerializeField] private Text _recDotText;
        [SerializeField] private Text _statusText;

        private void Awake()
        {
            RestoreMissingReferences();

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

        private void RestoreMissingReferences()
        {
            RestoreMissingReference(ref _canvasGroup, GetComponent<CanvasGroup>);
            RestoreMissingReference(ref _countdownGroup, () => FindChildGameObject(CountdownGroupName));
            RestoreMissingReference(ref _countdownText, () => FindChildComponent<Text>(CountdownTextName));
            RestoreMissingReference(ref _recordingGroup, () => FindChildGameObject(RecordingGroupName));
            RestoreMissingReference(ref _recDotText, () => FindChildComponent<Text>(RecDotName));
            RestoreMissingReference(ref _statusText, () => FindChildComponent<Text>(StatusTextName));
        }

        private void RestoreMissingReference<T>(ref T reference, System.Func<T> resolveReference)
            where T : UnityEngine.Object
        {
            if (reference != null)
            {
                return;
            }

            reference = resolveReference();
        }

        private GameObject FindChildGameObject(string childName)
        {
            Transform[] children = GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i].gameObject;
                }
            }

            return null;
        }

        private T FindChildComponent<T>(string childName) where T : Component
        {
            T[] children = GetComponentsInChildren<T>(true);
            for (int i = 0; i < children.Length; i++)
            {
                if (children[i].name == childName)
                {
                    return children[i];
                }
            }

            return null;
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
