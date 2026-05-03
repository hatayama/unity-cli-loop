#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop
{
    // Base class for replay verification controllers.
    // Provides event logging, log persistence, and frame-normalized comparison.
    // Subclasses override RecordEvents() to capture scene-specific state each frame.
    public abstract class ReplayVerificationControllerBase : MonoBehaviour
    {
        private const int TARGET_FRAME_RATE = 60;
        private const float ROUND_MULTIPLIER = 10000f;
        public const string LOG_OUTPUT_DIR = ".uloop/outputs/InputRecordings";
        public const string RECORDING_LOG_FILE = "recording-event-log.txt";
        public const string REPLAY_LOG_FILE = "replay-event-log.txt";

        [SerializeField] private GameObject? _verifyPanel;
        [SerializeField] private Text? _verifyResultText;

        private int _startFrame;
        private bool _activated;
        private readonly List<string> _eventLog = new();

        public int LastComparisonDiffCount { get; private set; } = -1;

        protected bool Activated => _activated;
        protected int RelativeFrame => Time.frameCount - _startFrame;
        protected List<string> EventLog => _eventLog;

        protected virtual void Start()
        {
            Debug.Assert(_verifyPanel != null, "_verifyPanel must be assigned in scene");
            Debug.Assert(_verifyResultText != null, "_verifyResultText must be assigned in scene");

            Application.targetFrameRate = TARGET_FRAME_RATE;
            _startFrame = Time.frameCount;
            HidePanel(_verifyPanel);
        }

        protected virtual void Update()
        {
            if (!_activated)
            {
                if (!TryActivateFromInput())
                {
                    return;
                }
                _activated = true;
            }

            int relativeFrame = RelativeFrame;
            if (relativeFrame < 0)
            {
                return;
            }

            RecordEvents(relativeFrame);
        }

        protected abstract bool TryActivateFromInput();

        protected abstract void RecordEvents(int relativeFrame);

        protected virtual void ResetState()
        {
            _eventLog.Clear();
        }

        public void ActivateForExternalControl()
        {
            Activate();
        }

        public void ActivateForExternalReplay()
        {
            // +2 instead of +1: InputReplayer injects state in onAfterUpdate,
            // which takes effect one frame later when Update() reads it.
            ResetState();
            _activated = true;
            _startFrame = Time.frameCount + 2;
            HidePanel(_verifyPanel);
        }

        public void OnReplayCompleted()
        {
            ShowPanel(_verifyPanel);
        }

        public void OnSaveRecordingLog()
        {
            SaveLog(GetLogPath(RECORDING_LOG_FILE));
            SetVerifyResult($"Recording log saved ({_eventLog.Count} entries)");
        }

        public void OnSaveReplayLog()
        {
            SaveLog(GetLogPath(REPLAY_LOG_FILE));
            SetVerifyResult($"Replay log saved ({_eventLog.Count} entries)");
        }

        public void OnCompareLogs()
        {
            string recordingPath = GetLogPath(RECORDING_LOG_FILE);
            string replayPath = GetLogPath(REPLAY_LOG_FILE);

            if (!File.Exists(recordingPath))
            {
                SetVerifyResult("Recording log not found. Save it first.");
                return;
            }

            if (!File.Exists(replayPath))
            {
                SetVerifyResult("Replay log not found. Save it first.");
                return;
            }

            string[] recordingLines = File.ReadAllLines(recordingPath);
            string[] replayLines = File.ReadAllLines(replayPath);

            if (recordingLines.Length == 0 && replayLines.Length == 0)
            {
                LastComparisonDiffCount = 0;
                SetVerifyResult("Both logs are empty.");
                return;
            }

            string[] normalizedRecording = NormalizeFrameNumbers(recordingLines);
            string[] normalizedReplay = NormalizeFrameNumbers(replayLines);

            int maxLines = Mathf.Max(normalizedRecording.Length, normalizedReplay.Length);
            int diffCount = 0;
            System.Text.StringBuilder sb = new System.Text.StringBuilder();

            for (int i = 0; i < maxLines; i++)
            {
                string recordLine = i < normalizedRecording.Length ? normalizedRecording[i] : "(missing)";
                string replayLine = i < normalizedReplay.Length ? normalizedReplay[i] : "(missing)";

                if (recordLine != replayLine)
                {
                    diffCount++;
                    if (diffCount <= 5)
                    {
                        sb.AppendLine($"L{i + 1}: Rec[{recordLine}] Rep[{replayLine}]");
                    }
                }
            }

            LastComparisonDiffCount = diffCount;

            if (diffCount == 0)
            {
                SetVerifyResult($"MATCH: {normalizedRecording.Length} events identical.\nReplay is accurate!");
            }
            else
            {
                string details = diffCount > 5 ? $"\n...and {diffCount - 5} more" : "";
                SetVerifyResult($"MISMATCH: {diffCount} differences\n(rec: {normalizedRecording.Length}, rep: {normalizedReplay.Length})\n{sb}{details}");
            }
        }

        public void ClearLog()
        {
            ResetState();
            HidePanel(_verifyPanel);
        }

        public void SaveLog(string path)
        {
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllLines(path, _eventLog);
            Debug.Log($"[ReplayVerification] Event log saved to {path} ({_eventLog.Count} entries)");
        }

        private void Activate()
        {
            ResetState();
            _activated = true;
            // +1: recorder/replayer first capture/inject at the next onAfterUpdate
            _startFrame = Time.frameCount + 1;
            HidePanel(_verifyPanel);
        }

        private void SetVerifyResult(string message)
        {
            _verifyResultText!.text = message;
            Debug.Log($"[ReplayVerification] {message}");
        }

        private static void ShowPanel(GameObject? panel)
        {
            if (panel != null) panel.SetActive(true);
        }

        private static void HidePanel(GameObject? panel)
        {
            if (panel != null) panel.SetActive(false);
        }

        private static string GetLogPath(string fileName)
        {
            return Path.Combine(LOG_OUTPUT_DIR, fileName);
        }

        protected static string[] NormalizeFrameNumbers(string[] lines)
        {
            if (lines.Length == 0)
            {
                return lines;
            }

            int firstFrame = ParseFrameNumber(lines[0]);
            string[] normalized = new string[lines.Length];

            for (int i = 0; i < lines.Length; i++)
            {
                int absoluteFrame = ParseFrameNumber(lines[i]);
                int relativeFrame = absoluteFrame - firstFrame;
                int colonIndex = lines[i].IndexOf(':');
                string content = colonIndex >= 0 ? lines[i].Substring(colonIndex + 2) : lines[i];
                normalized[i] = $"Frame {relativeFrame}: {content}";
            }

            return normalized;
        }

        private static int ParseFrameNumber(string line)
        {
            if (!line.StartsWith("Frame "))
            {
                return 0;
            }
            int colonIndex = line.IndexOf(':');
            if (colonIndex < 0)
            {
                return 0;
            }
            string frameStr = line.Substring(6, colonIndex - 6);
            if (int.TryParse(frameStr, out int frame))
            {
                return frame;
            }
            return 0;
        }

        protected static Vector3 RoundVector3(Vector3 v)
        {
            return new Vector3(
                Mathf.Round(v.x * ROUND_MULTIPLIER) / ROUND_MULTIPLIER,
                Mathf.Round(v.y * ROUND_MULTIPLIER) / ROUND_MULTIPLIER,
                Mathf.Round(v.z * ROUND_MULTIPLIER) / ROUND_MULTIPLIER
            );
        }

        protected static Vector2 RoundVector2(Vector2 v)
        {
            return new Vector2(
                Mathf.Round(v.x * ROUND_MULTIPLIER) / ROUND_MULTIPLIER,
                Mathf.Round(v.y * ROUND_MULTIPLIER) / ROUND_MULTIPLIER
            );
        }

        protected static string FormatVector3(Vector3 v)
        {
            return $"({v.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}, {v.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}, {v.z.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})";
        }

        protected static string FormatVector2(Vector2 v)
        {
            return $"({v.x.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)}, {v.y.ToString("F4", System.Globalization.CultureInfo.InvariantCulture)})";
        }
    }
}
#endif
