#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    public enum RecordingApplicationPhase
    {
        None = 0,
        Countdown = 1,
        Recording = 2
    }

    public readonly struct RecordingApplicationState
    {
        public readonly bool IsRecording;
        public readonly bool IsReplaying;
        public readonly RecordingApplicationPhase Phase;
        public readonly float RecordingElapsedSeconds;
        public readonly float CountdownRemainingSeconds;
        public readonly int ReplayCurrentFrame;
        public readonly int ReplayTotalFrames;

        public RecordingApplicationState(
            bool isRecording,
            bool isReplaying,
            RecordingApplicationPhase phase,
            float recordingElapsedSeconds,
            float countdownRemainingSeconds,
            int replayCurrentFrame,
            int replayTotalFrames)
        {
            IsRecording = isRecording;
            IsReplaying = isReplaying;
            Phase = phase;
            RecordingElapsedSeconds = recordingElapsedSeconds;
            CountdownRemainingSeconds = countdownRemainingSeconds;
            ReplayCurrentFrame = replayCurrentFrame;
            ReplayTotalFrames = replayTotalFrames;
        }
    }

    public readonly struct RecordingFileList
    {
        public readonly string[] FilePaths;
        public readonly string[] DisplayNames;

        public RecordingFileList(string[] filePaths, string[] displayNames)
        {
            Debug.Assert(filePaths != null, "filePaths must not be null");
            Debug.Assert(displayNames != null, "displayNames must not be null");

            FilePaths = filePaths ?? throw new ArgumentNullException(nameof(filePaths));
            DisplayNames = displayNames ?? throw new ArgumentNullException(nameof(displayNames));

            Debug.Assert(FilePaths.Length == DisplayNames.Length, "file and display arrays must have equal length");
        }
    }

    public readonly struct RecordingApplicationResult
    {
        public readonly bool ShouldShowDialog;
        public readonly string DialogMessage;

        private RecordingApplicationResult(bool shouldShowDialog, string dialogMessage)
        {
            ShouldShowDialog = shouldShowDialog;
            DialogMessage = dialogMessage;
        }

        public static RecordingApplicationResult NoDialog()
        {
            return new RecordingApplicationResult(false, "");
        }

        public static RecordingApplicationResult Dialog(string dialogMessage)
        {
            Debug.Assert(!string.IsNullOrEmpty(dialogMessage), "dialogMessage must not be null or empty");
            return new RecordingApplicationResult(true, dialogMessage);
        }
    }

    // Bridges the recordings window to input record/replay services without exposing service internals to Presentation.
    /// <summary>
    /// Provides Recordings Application operations for its owning module.
    /// </summary>
    public sealed class RecordingsApplicationService
    {
        private const string JsonFilePattern = "*.json";

        private int _countdownGeneration;

        public void AddReplayCompletedHandler(Action handler)
        {
            InputReplayer.AddReplayCompletedHandler(handler);
        }

        public void RemoveReplayCompletedHandler(Action handler)
        {
            InputReplayer.RemoveReplayCompletedHandler(handler);
        }

        public void AddRecordingStoppedHandler(Action handler)
        {
            InputRecorder.AddRecordingStoppedHandler(handler);
        }

        public void RemoveRecordingStoppedHandler(Action handler)
        {
            InputRecorder.RemoveRecordingStoppedHandler(handler);
        }

        public RecordingApplicationResult ToggleRecording(int requestedDelaySeconds)
        {
            if (!EditorApplication.isPlaying)
            {
                return RecordingApplicationResult.Dialog("PlayMode must be active to record.");
            }

            if (RecordInputOverlayState.Phase == RecordInputOverlayPhase.Countdown)
            {
                _countdownGeneration++;
                RecordInputOverlayState.Clear();
                return RecordingApplicationResult.NoDialog();
            }

            if (InputRecorder.IsRecording)
            {
                SaveCurrentRecording();
                return RecordingApplicationResult.NoDialog();
            }

            if (InputReplayer.IsReplaying)
            {
                return RecordingApplicationResult.Dialog("Cannot record while replaying.");
            }

            StartRecording(requestedDelaySeconds);
            return RecordingApplicationResult.NoDialog();
        }

        public RecordingApplicationResult ToggleReplay(string selectedFilePath)
        {
            if (!EditorApplication.isPlaying)
            {
                return RecordingApplicationResult.Dialog("PlayMode must be active to replay.");
            }

            if (InputReplayer.IsReplaying)
            {
                InputReplayer.StopReplay();
                return RecordingApplicationResult.NoDialog();
            }

            if (InputRecorder.IsRecording)
            {
                return RecordingApplicationResult.Dialog("Cannot replay while recording.");
            }

            if (string.IsNullOrEmpty(selectedFilePath))
            {
                return RecordingApplicationResult.Dialog("No recording file selected.");
            }

            if (!File.Exists(selectedFilePath))
            {
                return RecordingApplicationResult.Dialog("Recording file not found.");
            }

            InputRecordingData? data = InputRecordingFileHelper.Load(selectedFilePath);
            if (data == null)
            {
                return RecordingApplicationResult.Dialog("Failed to load recording file.");
            }

            OverlayCanvasFactory.EnsureExists();
            RecordReplayOverlayFactory.EnsureReplayOverlay();
            InputReplayer.StartReplay(data, false, true);
            return RecordingApplicationResult.NoDialog();
        }

        public RecordingApplicationState GetCurrentState()
        {
            return new RecordingApplicationState(
                InputRecorder.IsRecording,
                InputReplayer.IsReplaying,
                ToApplicationPhase(RecordInputOverlayState.Phase),
                RecordInputOverlayState.ElapsedSeconds,
                RecordInputOverlayState.RemainingSeconds,
                InputReplayer.CurrentFrame,
                InputReplayer.TotalFrames);
        }

        public RecordingFileList GetRecordingFiles()
        {
            string outputDirectory = RecordInputConstants.DEFAULT_OUTPUT_DIR;
            if (!Directory.Exists(outputDirectory))
            {
                return new RecordingFileList(Array.Empty<string>(), Array.Empty<string>());
            }

            string[] filePaths = Directory.GetFiles(outputDirectory, JsonFilePattern);
            Array.Sort(filePaths);
            Array.Reverse(filePaths);

            string[] displayNames = new string[filePaths.Length];
            for (int i = 0; i < filePaths.Length; i++)
            {
                displayNames[i] = Path.GetFileName(filePaths[i]);
            }

            return new RecordingFileList(filePaths, displayNames);
        }

        public string EnsureRecordingFolderExists()
        {
            string outputDirectory = RecordInputConstants.DEFAULT_OUTPUT_DIR;
            if (!Directory.Exists(outputDirectory))
            {
                Directory.CreateDirectory(outputDirectory);
            }

            return outputDirectory;
        }

        private void StartRecording(int requestedDelaySeconds)
        {
            int delaySeconds = Mathf.Clamp(
                requestedDelaySeconds,
                RecordInputConstants.MIN_DELAY_SECONDS,
                RecordInputConstants.MAX_DELAY_SECONDS);

            OverlayCanvasFactory.EnsureExists();
            RecordReplayOverlayFactory.EnsureRecordOverlay();

            if (delaySeconds <= 0)
            {
                RecordInputOverlayState.StartRecording();
                InputRecorder.StartRecording(null);
                return;
            }

            int generation = ++_countdownGeneration;
            RecordInputOverlayState.StartCountdown(delaySeconds);
            int delayMilliseconds = delaySeconds * 1000;
            _ = TimerDelay.WaitThenExecuteOnMainThread(delayMilliseconds, () => StartRecordingAfterCountdown(generation));
        }

        private void StartRecordingAfterCountdown(int generation)
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
        }

        private static void SaveCurrentRecording()
        {
            InputRecordingData data = InputRecorder.StopRecording();
            string outputPath = InputRecordingFileHelper.ResolveOutputPath("");
            InputRecordingFileHelper.Save(data, outputPath);
            InputRecorder.NotifyRecordingStopped();
        }

        private static RecordingApplicationPhase ToApplicationPhase(RecordInputOverlayPhase phase)
        {
            switch (phase)
            {
                case RecordInputOverlayPhase.Countdown:
                    return RecordingApplicationPhase.Countdown;
                case RecordInputOverlayPhase.Recording:
                    return RecordingApplicationPhase.Recording;
                default:
                    return RecordingApplicationPhase.None;
            }
        }
    }

    /// <summary>
    /// Provides a narrow facade over Recordings Application behavior.
    /// </summary>
    public static class RecordingsApplicationFacade
    {
        private static readonly RecordingsApplicationService ServiceValue = new RecordingsApplicationService();

        public static void AddReplayCompletedHandler(Action handler)
        {
            ServiceValue.AddReplayCompletedHandler(handler);
        }

        public static void RemoveReplayCompletedHandler(Action handler)
        {
            ServiceValue.RemoveReplayCompletedHandler(handler);
        }

        public static void AddRecordingStoppedHandler(Action handler)
        {
            ServiceValue.AddRecordingStoppedHandler(handler);
        }

        public static void RemoveRecordingStoppedHandler(Action handler)
        {
            ServiceValue.RemoveRecordingStoppedHandler(handler);
        }

        public static RecordingApplicationResult ToggleRecording(int requestedDelaySeconds)
        {
            return ServiceValue.ToggleRecording(requestedDelaySeconds);
        }

        public static RecordingApplicationResult ToggleReplay(string selectedFilePath)
        {
            return ServiceValue.ToggleReplay(selectedFilePath);
        }

        public static RecordingApplicationState GetCurrentState()
        {
            return ServiceValue.GetCurrentState();
        }

        public static RecordingFileList GetRecordingFiles()
        {
            return ServiceValue.GetRecordingFiles();
        }

        public static string EnsureRecordingFolderExists()
        {
            return ServiceValue.EnsureRecordingFolderExists();
        }
    }
}
#endif
