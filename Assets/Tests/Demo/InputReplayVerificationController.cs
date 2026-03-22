#if ULOOPMCP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP
{
    // Deterministic controller for verifying record/replay accuracy.
    // Uses fixed per-frame movement (no deltaTime) to ensure identical
    // results between recording and replay at the same frame rate.
    public class InputReplayVerificationController : MonoBehaviour
    {
        private const float MOVE_SPEED = 0.1f;
        private const float ROTATE_SENSITIVITY = 0.5f;
        private const float SCALE_STEP = 0.1f;
        private const int TARGET_FRAME_RATE = 60;
        private const float ROUND_MULTIPLIER = 10000f;
        private const string LOG_OUTPUT_DIR = ".uloop/outputs/InputRecordings";
        private const string RECORDING_LOG_FILE = "recording-event-log.txt";
        private const string REPLAY_LOG_FILE = "replay-event-log.txt";

        // Editor bridge subscribes to these to call InputRecorder/InputReplayer
        public static event Action? RecordingStartRequested;
        public static event Action? RecordingStopRequested;
        public static event Action? ReplayStartRequested;
        public static event Action? ReplayStopRequested;

        [SerializeField] private Text? _frameText;
        [SerializeField] private Text? _positionText;
        [SerializeField] private Text? _rotationText;
        [SerializeField] private Text? _scaleText;
        [SerializeField] private Text? _inputText;
        [SerializeField] private GameObject? _startPanel;
        [SerializeField] private GameObject? _stopPanel;
        [SerializeField] private GameObject? _verifyPanel;
        [SerializeField] private Text? _verifyResultText;
        [SerializeField] private MeshRenderer? _cubeRenderer;

        private Vector3 _initialPosition;
        private Vector3 _initialEulerAngles;
        private bool _isActive;
        private int _startFrame;
        private readonly List<string> _eventLog = new();
        private Vector3 _lastLoggedPosition;
        private bool _colorToggleRed;
        private bool _colorToggleBlue;

        private void Start()
        {
            Debug.Assert(_startPanel != null, "_startPanel must be assigned in scene");
            Debug.Assert(_stopPanel != null, "_stopPanel must be assigned in scene");
            Debug.Assert(_verifyPanel != null, "_verifyPanel must be assigned in scene");
            Debug.Assert(_cubeRenderer != null, "_cubeRenderer must be assigned in scene");

            Application.targetFrameRate = TARGET_FRAME_RATE;
            _initialPosition = transform.position;
            _initialEulerAngles = transform.eulerAngles;
            ShowPanel(_startPanel);
            HidePanel(_stopPanel);
            HidePanel(_verifyPanel);
        }

        private void Update()
        {
            if (!_isActive)
            {
                return;
            }

            Keyboard? keyboard = Keyboard.current;
            Mouse? mouse = Mouse.current;
            if (keyboard == null || mouse == null)
            {
                return;
            }

            int relativeFrame = Time.frameCount - _startFrame;

            ProcessMovement(keyboard, relativeFrame);
            ProcessRotation(mouse, relativeFrame);
            ProcessClicks(mouse, relativeFrame);
            ProcessScroll(mouse, relativeFrame);
            UpdateUI(keyboard, mouse, relativeFrame);
        }

        // Called by UI Button "Start Recording"
        public void OnStartRecording()
        {
            Activate();
            RecordingStartRequested?.Invoke();
        }

        // Called by UI Button "Start Replay"
        public void OnStartReplay()
        {
            Activate();
            // First onAfterUpdate injection happens next frame because
            // this frame's onAfterUpdate already fired before this button click.
            _startFrame = Time.frameCount + 1;
            ReplayStartRequested?.Invoke();
        }

        // Resets state and accepts input without triggering record/replay.
        // Use when record/replay is started externally (e.g. via CLI).
        public void ActivateForExternalControl()
        {
            Activate();
        }

        // Same as ActivateForExternalControl but with the 1-frame offset
        // needed when replay injection starts next frame.
        public void ActivateForExternalReplay()
        {
            Activate();
            _startFrame = Time.frameCount + 1;
        }

        // Called by UI Button "Stop"
        public void OnStop()
        {
            _isActive = false;
            RecordingStopRequested?.Invoke();
            ReplayStopRequested?.Invoke();
            ShowPostSessionUI();
        }

        // Called by EditorBridge via SendMessage when replay finishes
        public void OnReplayCompleted()
        {
            _isActive = false;
            ShowPostSessionUI();
        }

        private void ShowPostSessionUI()
        {
            HidePanel(_stopPanel);
            ShowPanel(_startPanel);
            ShowPanel(_verifyPanel);
        }

        private void Activate()
        {
            ResetState();
            _isActive = true;
            _startFrame = Time.frameCount;
            HidePanel(_startPanel);
            ShowPanel(_stopPanel);
            HidePanel(_verifyPanel);
        }

        private void ResetState()
        {
            transform.position = _initialPosition;
            transform.eulerAngles = _initialEulerAngles;
            transform.localScale = Vector3.one;
            _colorToggleRed = false;
            _colorToggleBlue = false;
            UpdateCubeColor();
            _eventLog.Clear();
            _lastLoggedPosition = _initialPosition;
        }

        private static void ShowPanel(GameObject? panel)
        {
            if (panel != null) panel.SetActive(true);
        }

        private static void HidePanel(GameObject? panel)
        {
            if (panel != null) panel.SetActive(false);
        }

        private void ProcessMovement(Keyboard keyboard, int frame)
        {
            Vector3 movement = Vector3.zero;

            if (keyboard[Key.W].isPressed) movement.z += MOVE_SPEED;
            if (keyboard[Key.S].isPressed) movement.z -= MOVE_SPEED;
            if (keyboard[Key.A].isPressed) movement.x -= MOVE_SPEED;
            if (keyboard[Key.D].isPressed) movement.x += MOVE_SPEED;

            if (movement == Vector3.zero)
            {
                return;
            }

            transform.Translate(movement, Space.World);

            // Rounding avoids float noise that would make logs differ between runs
            Vector3 rounded = RoundVector3(transform.position);
            if (rounded != _lastLoggedPosition)
            {
                _eventLog.Add($"Frame {frame}: Position {FormatVector3(rounded)}");
                _lastLoggedPosition = rounded;
            }
        }

        private void ProcessRotation(Mouse mouse, int frame)
        {
            Vector2 delta = mouse.delta.ReadValue();
            if (delta == Vector2.zero)
            {
                return;
            }

            float rotationY = delta.x * ROTATE_SENSITIVITY;
            Vector3 euler = transform.eulerAngles;
            euler.y += rotationY;
            transform.eulerAngles = euler;

            _eventLog.Add($"Frame {frame}: Rotation Y={euler.y.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        private void ProcessClicks(Mouse mouse, int frame)
        {
            if (mouse.leftButton.wasPressedThisFrame)
            {
                _colorToggleRed = !_colorToggleRed;
                UpdateCubeColor();
                _eventLog.Add($"Frame {frame}: LeftClick color={GetColorName()}");
            }

            if (mouse.rightButton.wasPressedThisFrame)
            {
                _colorToggleBlue = !_colorToggleBlue;
                UpdateCubeColor();
                _eventLog.Add($"Frame {frame}: RightClick color={GetColorName()}");
            }
        }

        private void ProcessScroll(Mouse mouse, int frame)
        {
            float scrollY = mouse.scroll.y.ReadValue();
            if (scrollY == 0f)
            {
                return;
            }

            float direction = scrollY > 0f ? SCALE_STEP : -SCALE_STEP;
            Vector3 scale = transform.localScale;
            float newScale = Mathf.Max(0.1f, scale.x + direction);
            transform.localScale = Vector3.one * newScale;

            _eventLog.Add($"Frame {frame}: Scroll scale={newScale.ToString("F2", CultureInfo.InvariantCulture)}");
        }

        private void UpdateCubeColor()
        {
            if (_cubeRenderer == null)
            {
                return;
            }

            Color color = Color.white;
            if (_colorToggleRed) color = Color.red;
            if (_colorToggleBlue) color = Color.blue;
            if (_colorToggleRed && _colorToggleBlue) color = Color.magenta;

            _cubeRenderer.material.color = color;
        }

        private string GetColorName()
        {
            if (_colorToggleRed && _colorToggleBlue) return "Magenta";
            if (_colorToggleRed) return "Red";
            if (_colorToggleBlue) return "Blue";
            return "White";
        }

        private void UpdateUI(Keyboard keyboard, Mouse mouse, int frame)
        {
            if (_frameText != null) _frameText.text = $"Frame: {frame}";
            if (_positionText != null) _positionText.text = $"Pos: {FormatVector3(transform.position)}";
            if (_rotationText != null) _rotationText.text = $"Rot Y: {transform.eulerAngles.y:F2}";
            if (_scaleText != null) _scaleText.text = $"Scale: {transform.localScale.x:F2}";
            if (_inputText != null) _inputText.text = BuildInputStateText(keyboard, mouse);
        }

        private static string BuildInputStateText(Keyboard keyboard, Mouse mouse)
        {
            List<string> held = new List<string>();
            if (keyboard[Key.W].isPressed) held.Add("W");
            if (keyboard[Key.A].isPressed) held.Add("A");
            if (keyboard[Key.S].isPressed) held.Add("S");
            if (keyboard[Key.D].isPressed) held.Add("D");
            if (mouse.leftButton.isPressed) held.Add("LMB");
            if (mouse.rightButton.isPressed) held.Add("RMB");

            return held.Count > 0 ? $"Input: [{string.Join(", ", held)}]" : "Input: [none]";
        }

        public void SaveLog(string path)
        {
            string directory = Path.GetDirectoryName(path)!;
            Directory.CreateDirectory(directory);
            File.WriteAllLines(path, _eventLog);
            Debug.Log($"[InputReplayVerification] Event log saved to {path} ({_eventLog.Count} entries)");
        }

        // Called by UI Button "Save Recording Log"
        public void OnSaveRecordingLog()
        {
            SaveLog(GetLogPath(RECORDING_LOG_FILE));
            SetVerifyResult($"Recording log saved ({_eventLog.Count} entries)");
        }

        // Called by UI Button "Save Replay Log"
        public void OnSaveReplayLog()
        {
            SaveLog(GetLogPath(REPLAY_LOG_FILE));
            SetVerifyResult($"Replay log saved ({_eventLog.Count} entries)");
        }

        // Called by UI Button "Compare Logs"
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
            _isActive = false;
            ResetState();
            ShowPanel(_startPanel);
            HidePanel(_stopPanel);
            HidePanel(_verifyPanel);
        }

        private static string GetLogPath(string fileName)
        {
            return System.IO.Path.Combine(LOG_OUTPUT_DIR, fileName);
        }

        private void SetVerifyResult(string message)
        {
            if (_verifyResultText != null) _verifyResultText.text = message;
            Debug.Log($"[InputReplayVerification] {message}");
        }

        // Normalizes absolute frame numbers to relative (first event = frame 0).
        // CLI commands introduce variable delays between controller activation
        // and record/replay start, so absolute frame numbers differ. Relative
        // frame numbers preserve inter-event timing for accurate comparison.
        private static string[] NormalizeFrameNumbers(string[] lines)
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
            // "Frame 123: ..."
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

        private static Vector3 RoundVector3(Vector3 v)
        {
            return new Vector3(
                Mathf.Round(v.x * ROUND_MULTIPLIER) / ROUND_MULTIPLIER,
                Mathf.Round(v.y * ROUND_MULTIPLIER) / ROUND_MULTIPLIER,
                Mathf.Round(v.z * ROUND_MULTIPLIER) / ROUND_MULTIPLIER
            );
        }

        private static string FormatVector3(Vector3 v)
        {
            return $"({v.x.ToString("F4", CultureInfo.InvariantCulture)}, {v.y.ToString("F4", CultureInfo.InvariantCulture)}, {v.z.ToString("F4", CultureInfo.InvariantCulture)})";
        }
    }
}
#endif
