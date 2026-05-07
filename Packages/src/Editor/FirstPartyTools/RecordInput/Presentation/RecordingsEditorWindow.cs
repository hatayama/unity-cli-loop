#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Defines the Unity Editor window for Recordings Editor workflows.
    /// </summary>
    public class RecordingsEditorWindow : EditorWindow
    {
        private const string UXML_RELATIVE_PATH = "Editor/Presentation/Recordings/RecordingsEditorWindow.uxml";
        private const string USS_RELATIVE_PATH = "Editor/Presentation/Recordings/RecordingsEditorWindow.uss";

        private Button _recordButton;
        private Button _replayButton;
        private Button _openFolderButton;
        private IntegerField _delayField;
        private DropdownField _fileDropdown;
        private Label _recordStatusLabel;
        private Label _replayStatusLabel;
        private VisualElement _recordStatusIndicator;
        private VisualElement _replayStatusIndicator;

        private string[] _recordingFiles = Array.Empty<string>();

        private bool _prevIsRecording;
        private bool _prevIsReplaying;
        private RecordingApplicationPhase _prevPhase;
        private int _prevMinutes = -1;
        private int _prevSecs = -1;
        private int _prevReplayFrame = -1;

        [MenuItem("Window/Unity CLI Loop/Recordings", priority = 1)]
        public static void ShowWindow()
        {
            RecordingsEditorWindow window = GetWindow<RecordingsEditorWindow>("Recordings");
            window.Show();
        }

        private void CreateGUI()
        {
            string uxmlPath = $"{UnityCliLoopConstants.PackageAssetPath}/{UXML_RELATIVE_PATH}";
            VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(uxmlPath);
            Debug.Assert(uxml != null, "UXML not found at " + uxmlPath);
            uxml.CloneTree(rootVisualElement);

            string ussPath = $"{UnityCliLoopConstants.PackageAssetPath}/{USS_RELATIVE_PATH}";
            StyleSheet uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(ussPath);
            Debug.Assert(uss != null, "USS not found at " + ussPath);
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
            RecordingsApplicationFacade.AddReplayCompletedHandler(OnReplayCompleted);
            RecordingsApplicationFacade.AddRecordingStoppedHandler(OnRecordingStopped);
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            RecordingsApplicationFacade.RemoveReplayCompletedHandler(OnReplayCompleted);
            RecordingsApplicationFacade.RemoveRecordingStoppedHandler(OnRecordingStopped);
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
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
            RecordingApplicationResult result = RecordingsApplicationFacade.ToggleRecording(_delayField.value);
            ShowDialogWhenNeeded(result);
        }

        private void OnReplayButtonClicked()
        {
            string selectedFile = GetSelectedFilePath();
            RecordingApplicationResult result = RecordingsApplicationFacade.ToggleReplay(selectedFile);
            ShowDialogWhenNeeded(result);
        }

        private void OnOpenFolderClicked()
        {
            string outputDirectory = RecordingsApplicationFacade.EnsureRecordingFolderExists();
            EditorUtility.RevealInFinder(outputDirectory);
        }

        private void OnRecordingStopped()
        {
            RefreshFileList();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode || state == PlayModeStateChange.EnteredEditMode)
            {
                RefreshFileList();
            }
        }

        private void OnReplayCompleted()
        {
            UpdateReplayUI();
        }

        private void OnEditorUpdate()
        {
            RecordingApplicationState state = RecordingsApplicationFacade.GetCurrentState();

            bool stateChanged = state.IsRecording != _prevIsRecording
                || state.IsReplaying != _prevIsReplaying
                || state.Phase != _prevPhase;

            if (!state.IsRecording
                && !state.IsReplaying
                && state.Phase == RecordingApplicationPhase.None
                && !stateChanged)
            {
                return;
            }

            _prevIsRecording = state.IsRecording;
            _prevIsReplaying = state.IsReplaying;
            _prevPhase = state.Phase;

            UpdateRecordUI(state);
            UpdateReplayUI(state);
        }

        private void UpdateRecordUI()
        {
            UpdateRecordUI(RecordingsApplicationFacade.GetCurrentState());
        }

        private void UpdateRecordUI(RecordingApplicationState state)
        {
            if (_recordButton == null)
            {
                return;
            }

            if (state.IsRecording)
            {
                _recordButton.text = "Stop Recording";
                _recordButton.RemoveFromClassList("rec-button--record");
                _recordButton.AddToClassList("rec-button--recording");
                float elapsed = state.RecordingElapsedSeconds;
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
            else if (state.Phase == RecordingApplicationPhase.Countdown)
            {
                _recordButton.text = "Cancel";
                _recordButton.RemoveFromClassList("rec-button--recording");
                _recordButton.AddToClassList("rec-button--record");
                int remaining = Mathf.CeilToInt(state.CountdownRemainingSeconds);
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
            UpdateReplayUI(RecordingsApplicationFacade.GetCurrentState());
        }

        private void UpdateReplayUI(RecordingApplicationState state)
        {
            if (_replayButton == null)
            {
                return;
            }

            if (state.IsReplaying)
            {
                _replayButton.text = "Stop Replay";
                _replayButton.RemoveFromClassList("rec-button--replay");
                _replayButton.AddToClassList("rec-button--replaying");
                int current = state.ReplayCurrentFrame;
                int total = state.ReplayTotalFrames;
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
            RecordingFileList fileList = RecordingsApplicationFacade.GetRecordingFiles();
            _recordingFiles = fileList.FilePaths;

            if (_fileDropdown != null)
            {
                _fileDropdown.choices = new List<string>(fileList.DisplayNames);
                if (fileList.DisplayNames.Length > 0)
                {
                    _fileDropdown.index = 0;
                }
            }
        }

        private string GetSelectedFilePath()
        {
            if (_fileDropdown == null || _fileDropdown.index < 0 || _fileDropdown.index >= _recordingFiles.Length)
            {
                return "";
            }
            return _recordingFiles[_fileDropdown.index];
        }

        private static void ShowDialogWhenNeeded(RecordingApplicationResult result)
        {
            if (!result.ShouldShowDialog)
            {
                return;
            }

            EditorUtility.DisplayDialog("Recordings", result.DialogMessage, "OK");
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
#endif
