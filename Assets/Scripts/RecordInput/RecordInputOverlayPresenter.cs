using System.Globalization;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    public class RecordInputOverlayPresenter : MonoBehaviour
    {
        private const float FADE_OUT_DURATION = 0.5f;
        private const float DOT_BLINK_SPEED = 2f;

        [SerializeField] private RecordInputOverlayView _view;

        private bool _wasActive;
        private float _deactivatedTime;
        private int _lastMinutes = -1;
        private int _lastSecs = -1;
        private string _cachedStatusText = "";

        private void Awake()
        {
            Debug.Assert(_view != null, "_view must be assigned in prefab");
        }

        private void LateUpdate()
        {
            RecordInputOverlayPhase phase = RecordInputOverlayState.Phase;

            if (phase != RecordInputOverlayPhase.None)
            {
                _wasActive = true;
                _deactivatedTime = 0f;

                switch (phase)
                {
                    case RecordInputOverlayPhase.Countdown:
                        int remaining = Mathf.CeilToInt(RecordInputOverlayState.RemainingSeconds);
                        _view.ShowCountdown(remaining);
                        break;

                    case RecordInputOverlayPhase.Recording:
                        string statusText = FormatStatusText(RecordInputOverlayState.ElapsedSeconds);
                        float dotAlpha = Mathf.PingPong(Time.realtimeSinceStartup * DOT_BLINK_SPEED, 1f);
                        _view.ShowRecording(statusText, dotAlpha);
                        break;
                }

                return;
            }

            if (!_wasActive)
            {
                return;
            }

            if (_deactivatedTime <= 0f)
            {
                _deactivatedTime = Time.realtimeSinceStartup;
                _view.ShowStopped();
                return;
            }

            float fadingElapsed = Time.realtimeSinceStartup - _deactivatedTime;
            if (fadingElapsed > FADE_OUT_DURATION)
            {
                _view.Hide();
                _wasActive = false;
                _deactivatedTime = 0f;
                return;
            }

            float alpha = 1f - (fadingElapsed / FADE_OUT_DURATION);
            _view.SetAlpha(alpha);
        }

        private string FormatStatusText(float seconds)
        {
            int minutes = (int)(seconds / 60f);
            int secs = (int)(seconds % 60f);
            if (minutes == _lastMinutes && secs == _lastSecs)
            {
                return _cachedStatusText;
            }
            _lastMinutes = minutes;
            _lastSecs = secs;
            _cachedStatusText = string.Format(CultureInfo.InvariantCulture, "REC  {0:D2}:{1:D2}", minutes, secs);
            return _cachedStatusText;
        }
    }
}
