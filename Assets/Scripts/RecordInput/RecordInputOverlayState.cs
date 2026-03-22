using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    public enum RecordInputOverlayPhase
    {
        None,
        Countdown,
        Recording
    }

    public static class RecordInputOverlayState
    {
        private static RecordInputOverlayPhase _phase;
        private static float _countdownEndTime;
        private static float _recordingStartTime;

        public static RecordInputOverlayPhase Phase => _phase;

        public static float RemainingSeconds
        {
            get
            {
                if (_phase != RecordInputOverlayPhase.Countdown)
                {
                    return 0f;
                }
                float remaining = _countdownEndTime - Time.realtimeSinceStartup;
                return remaining > 0f ? remaining : 0f;
            }
        }

        public static float ElapsedSeconds
        {
            get
            {
                if (_phase != RecordInputOverlayPhase.Recording)
                {
                    return 0f;
                }
                return Time.realtimeSinceStartup - _recordingStartTime;
            }
        }

        public static void StartCountdown(float durationSeconds)
        {
            _phase = RecordInputOverlayPhase.Countdown;
            _countdownEndTime = Time.realtimeSinceStartup + durationSeconds;
        }

        public static void StartRecording()
        {
            _phase = RecordInputOverlayPhase.Recording;
            _recordingStartTime = Time.realtimeSinceStartup;
        }

        public static void Clear()
        {
            _phase = RecordInputOverlayPhase.None;
            _countdownEndTime = 0f;
            _recordingStartTime = 0f;
        }
    }
}
