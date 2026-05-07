using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    public enum RecordInputOverlayPhase
    {
        None,
        Countdown,
        Recording
    }

    /// <summary>
    /// Provides Record Input Overlay State operations for its owning module.
    /// </summary>
    public sealed class RecordInputOverlayStateService
    {
        private RecordInputOverlayPhase _phase;
        private float _countdownEndTime;
        private float _recordingStartTime;

        public RecordInputOverlayPhase Phase => _phase;

        public float RemainingSeconds
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

        public float ElapsedSeconds
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

        public void StartCountdown(float durationSeconds)
        {
            _phase = RecordInputOverlayPhase.Countdown;
            _countdownEndTime = Time.realtimeSinceStartup + durationSeconds;
        }

        public void StartRecording()
        {
            _phase = RecordInputOverlayPhase.Recording;
            _recordingStartTime = Time.realtimeSinceStartup;
        }

        public void Clear()
        {
            _phase = RecordInputOverlayPhase.None;
            _countdownEndTime = 0f;
            _recordingStartTime = 0f;
        }
    }

    /// <summary>
    /// Stores Record Input Overlay state shared by the owning workflow.
    /// </summary>
    public static class RecordInputOverlayState
    {
        private static readonly RecordInputOverlayStateService ServiceValue = new RecordInputOverlayStateService();

        public static RecordInputOverlayPhase Phase => ServiceValue.Phase;

        public static float RemainingSeconds => ServiceValue.RemainingSeconds;

        public static float ElapsedSeconds => ServiceValue.ElapsedSeconds;

        public static void StartCountdown(float durationSeconds)
        {
            ServiceValue.StartCountdown(durationSeconds);
        }

        public static void StartRecording()
        {
            ServiceValue.StartRecording();
        }

        public static void Clear()
        {
            ServiceValue.Clear();
        }
    }
}
