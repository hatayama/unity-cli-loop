using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace io.github.hatayama.uLoopMCP
{
    public class RecordingsEditorWindow : EditorWindow
    {
        private const string UXML_PATH = "Assets/Editor/CustomCommandSamples/Recordings/RecordingsEditorWindow.uxml";
        private const string USS_PATH = "Assets/Editor/CustomCommandSamples/Recordings/RecordingsEditorWindow.uss";

        private Button _recordButton;
        private Button _replayButton;
        private Button _openFolderButton;
        private IntegerField _delayField;
        private DropdownField _fileDropdown;
        private Label _recordStatusLabel;
        private Label _replayStatusLabel;
        private VisualElement _recordStatusIndicator;
        private VisualElement _replayStatusIndicator;

        private List<string> _recordingFiles = new();

        private bool _prevIsRecording;
        private bool _prevIsReplaying;
        private RecordInputOverlayPhase _prevPhase;
        private int _prevMinutes = -1;
        private int _prevSecs = -1;
        private int _prevReplayFrame = -1;
        private int _countdownGeneration;

        [MenuItem("uLoopMCP/Recordings")]
        public static void ShowWindow()
        {
            RecordingsEditorWindow window = GetWindow<RecordingsEditorWindow>("Recordings");
            window.Show();
        }

        private void CreateGUI()
        {
            VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);
            Debug.Assert(uxml != null, "UXML not found at " + UXML_PATH);
            uxml.CloneTree(rootVisualElement);

            StyleSheet uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(USS_PATH);
            Debug.Assert(uss != null, "USS not found at " + USS_PATH);
            rootVisualElement.styleSheets.Add(uss);

            QueryElements();
            BindEvents();
            RefreshFileList();
            UpdateRecordUI();
            UpdateReplayUI();
        }

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            InputReplayer.ReplayCompleted += OnReplayCompleted;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            InputReplayer.ReplayCompleted -= OnReplayCompleted;
            UnbindEvents();
        }

        private void UnbindEvents()
        {
            if (_recordButton != null) _recordButton.clicked -= OnRecordButtonClicked;
            if (_replayButton != null) _replayButton.clicked -= OnReplayButtonClicked;
            if (_openFolderButton != null) _openFolderButton.clicked -= OnOpenFolderClicked;
        }

        private void QueryElements()
        {
            _recordButton = rootVisualElement.Q<Button>("record-button");
            _replayButton = rootVisualElement.Q<Button>("replay-button");
            _openFolderButton = rootVisualElement.Q<Button>("open-folder-button");
            _delayField = rootVisualElement.Q<IntegerField>("delay-field");
            _fileDropdown = rootVisualElement.Q<DropdownField>("file-dropdown");
            _recordStatusLabel = rootVisualElement.Q<Label>("record-status-label");
            _replayStatusLabel = rootVisualElement.Q<Label>("replay-status-label");
            _recordStatusIndicator = rootVisualElement.Q("record-status-indicator");
            _replayStatusIndicator = rootVisualElement.Q("replay-status-indicator");
        }

        private void BindEvents()
        {
            _recordButton.clicked += OnRecordButtonClicked;
            _replayButton.clicked += OnReplayButtonClicked;
            _openFolderButton.clicked += OnOpenFolderClicked;
        }

        private void OnRecordButtonClicked()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Recordings", "PlayMode must be active to record.", "OK");
                return;
            }

            if (RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
            {
                _countdownGeneration++;
                RecordInputOverlayState.Clear();
                return;
            }

            if (InputRecorder.IsRecording)
            {
                InputRecordingData data = InputRecorder.StopRecording();
                string outputPath = InputRecordingFileHelper.ResolveOutputPath("");
                InputRecordingFileHelper.Save(data, outputPath);
                RefreshFileList();
                return;
            }

            if (InputReplayer.IsReplaying)
            {
                EditorUtility.DisplayDialog("Recordings", "Cannot record while replaying.", "OK");
                return;
            }

            int delaySeconds = Mathf.Clamp(_delayField.value, RecordInputConstants.MIN_DELAY_SECONDS, RecordInputConstants.MAX_DELAY_SECONDS);

            OverlayCanvasFactory.EnsureExists();
            RecordReplayOverlayFactory.EnsureRecordOverlay();

            if (delaySeconds > 0)
            {
                int generation = ++_countdownGeneration;
                RecordInputOverlayState.StartCountdown(delaySeconds);
                int delayMs = delaySeconds * 1000;
                TimerDelay.WaitThenExecuteOnMainThread(delayMs, () =>
                {
                    if (!EditorApplication.isPlaying
                        || generation != _countdownGeneration
                        || RecordInputOverlayState.Phase != RecordInputOverlayPhase.Countdown)
                    {
                        RecordInputOverlayState.Clear();
                        return;
                    }
                    RecordInputOverlayState.StartRecording();
                    InputRecorder.StartRecording(null);
                });
            }
            else
            {
                RecordInputOverlayState.StartRecording();
                InputRecorder.StartRecording(null);
            }
        }

        private void OnReplayButtonClicked()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("Recordings", "PlayMode must be active to replay.", "OK");
                return;
            }

            if (InputReplayer.IsReplaying)
            {
                InputReplayer.StopReplay();
                return;
            }

            if (InputRecorder.IsRecording)
            {
                EditorUtility.DisplayDialog("Recordings", "Cannot replay while recording.", "OK");
                return;
            }

            string selectedFile = GetSelectedFilePath();
            if (string.IsNullOrEmpty(selectedFile))
            {
                EditorUtility.DisplayDialog("Recordings", "No recording file selected.", "OK");
                return;
            }

            InputRecordingData data = InputRecordingFileHelper.Load(selectedFile);
            if (data == null)
            {
                EditorUtility.DisplayDialog("Recordings", "Failed to load recording file.", "OK");
                return;
            }

            OverlayCanvasFactory.EnsureExists();
            RecordReplayOverlayFactory.EnsureReplayOverlay();
            InputReplayer.StartReplay(data, false, true);
        }

        private void OnOpenFolderClicked()
        {
            string dir = RecordInputConstants.DEFAULT_OUTPUT_DIR;
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            EditorUtility.RevealInFinder(dir);
        }

        private void OnReplayCompleted()
        {
            UpdateReplayUI();
        }

        private void OnEditorUpdate()
        {
            bool isRecording = InputRecorder.IsRecording;
            bool isReplaying = InputReplayer.IsReplaying;
            RecordInputOverlayPhase phase = RecordInputOverlayState.Phase;

            bool stateChanged = isRecording != _prevIsRecording
                || isReplaying != _prevIsReplaying
                || phase != _prevPhase;

            if (!isRecording && !isReplaying && phase == RecordInputOverlayPhase.None && !stateChanged)
            {
                return;
            }

            _prevIsRecording = isRecording;
            _prevIsReplaying = isReplaying;
            _prevPhase = phase;

            UpdateRecordUI();
            UpdateReplayUI();
        }

        private void UpdateRecordUI()
        {
            if (_recordButton == null)
            {
                return;
            }

            bool isRecording = InputRecorder.IsRecording;
            RecordInputOverlayPhase phase = RecordInputOverlayState.Phase;

            if (isRecording)
            {
                _recordButton.text = "Stop Recording";
                _recordButton.RemoveFromClassList("rec-button--record");
                _recordButton.AddToClassList("rec-button--recording");
                float elapsed = RecordInputOverlayState.ElapsedSeconds;
                int minutes = (int)(elapsed / 60f);
                int secs = (int)(elapsed % 60f);
                if (minutes != _prevMinutes || secs != _prevSecs)
                {
                    _prevMinutes = minutes;
                    _prevSecs = secs;
                    _recordStatusLabel.text = $"Recording {minutes:D2}:{secs:D2}";
                }
                SetIndicatorClass(_recordStatusIndicator, "rec-status-indicator--recording");
            }
            else if (phase == RecordInputOverlayPhase.Countdown)
            {
                _recordButton.text = "Cancel";
                _recordButton.RemoveFromClassList("rec-button--recording");
                _recordButton.AddToClassList("rec-button--record");
                int remaining = Mathf.CeilToInt(RecordInputOverlayState.RemainingSeconds);
                _recordStatusLabel.text = $"Starting in {remaining}...";
                SetIndicatorClass(_recordStatusIndicator, "rec-status-indicator--countdown");
            }
            else
            {
                _recordButton.text = "Start Recording";
                _recordButton.RemoveFromClassList("rec-button--recording");
                _recordButton.AddToClassList("rec-button--record");
                _recordStatusLabel.text = "Idle";
                SetIndicatorClass(_recordStatusIndicator, null);
            }
        }

        private void UpdateReplayUI()
        {
            if (_replayButton == null)
            {
                return;
            }

            bool isReplaying = InputReplayer.IsReplaying;

            if (isReplaying)
            {
                _replayButton.text = "Stop Replay";
                _replayButton.RemoveFromClassList("rec-button--replay");
                _replayButton.AddToClassList("rec-button--replaying");
                int current = InputReplayer.CurrentFrame;
                int total = InputReplayer.TotalFrames;
                if (current != _prevReplayFrame)
                {
                    _prevReplayFrame = current;
                    _replayStatusLabel.text = $"Replay {current} / {total}";
                }
                SetIndicatorClass(_replayStatusIndicator, "rec-status-indicator--replaying");
            }
            else
            {
                _replayButton.text = "Start Replay";
                _replayButton.RemoveFromClassList("rec-button--replaying");
                _replayButton.AddToClassList("rec-button--replay");
                _replayStatusLabel.text = "Idle";
                SetIndicatorClass(_replayStatusIndicator, null);
            }
        }

        private void RefreshFileList()
        {
            _recordingFiles.Clear();

            string dir = RecordInputConstants.DEFAULT_OUTPUT_DIR;
            if (Directory.Exists(dir))
            {
                string[] files = Directory.GetFiles(dir, "*.json");
                Array.Sort(files);
                Array.Reverse(files);
                _recordingFiles.AddRange(files);
            }

            List<string> displayNames = _recordingFiles.Select(Path.GetFileName).ToList();

            if (_fileDropdown != null)
            {
                _fileDropdown.choices = displayNames;
                if (displayNames.Count > 0)
                {
                    _fileDropdown.index = 0;
                }
            }
        }

        private string GetSelectedFilePath()
        {
            if (_fileDropdown == null || _fileDropdown.index < 0 || _fileDropdown.index >= _recordingFiles.Count)
            {
                return "";
            }
            return _recordingFiles[_fileDropdown.index];
        }

        private static void SetIndicatorClass(VisualElement indicator, string activeClass)
        {
            indicator.RemoveFromClassList("rec-status-indicator--recording");
            indicator.RemoveFromClassList("rec-status-indicator--replaying");
            indicator.RemoveFromClassList("rec-status-indicator--countdown");

            if (!string.IsNullOrEmpty(activeClass))
            {
                indicator.AddToClassList(activeClass);
            }
        }
    }
}
