#if ULOOPMCP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    // Bridges the Runtime verification controller's UI events to
    // Editor-only InputRecorder/InputReplayer. Necessary because
    // the controller lives in a Runtime assembly and cannot reference
    // Editor classes directly.
    [InitializeOnLoad]
    internal static class InputReplayVerificationEditorBridge
    {
        static InputReplayVerificationEditorBridge()
        {
            InputReplayVerificationController.RecordingStartRequested -= OnRecordingStartRequested;
            InputReplayVerificationController.RecordingStartRequested += OnRecordingStartRequested;

            InputReplayVerificationController.RecordingStopRequested -= OnRecordingStopRequested;
            InputReplayVerificationController.RecordingStopRequested += OnRecordingStopRequested;

            InputReplayVerificationController.ReplayStartRequested -= OnReplayStartRequested;
            InputReplayVerificationController.ReplayStartRequested += OnReplayStartRequested;

            InputReplayVerificationController.ReplayStopRequested -= OnReplayStopRequested;
            InputReplayVerificationController.ReplayStopRequested += OnReplayStopRequested;

            InputReplayer.ReplayCompleted -= OnReplayCompleted;
            InputReplayer.ReplayCompleted += OnReplayCompleted;
        }

        private static void OnRecordingStartRequested()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            InputRecorder.StartRecording(keyFilter: null);
            Debug.Log("[VerificationBridge] Recording started");
        }

        private static void OnRecordingStopRequested()
        {
            if (!InputRecorder.IsRecording)
            {
                return;
            }

            InputRecordingData data = InputRecorder.StopRecording();
            string outputPath = InputRecordingFileHelper.ResolveOutputPath("");
            InputRecordingFileHelper.Save(data, outputPath);

            int eventCount = data.GetTotalEventCount();
            Debug.Log($"[VerificationBridge] Recording stopped: {eventCount} events, {data.Metadata.TotalFrames} frames -> {outputPath}");

            InputReplayVerificationController? controller = Object.FindObjectOfType<InputReplayVerificationController>();
            if (controller != null)
            {
                controller.OnSaveRecordingLog();
            }
        }

        private static void OnReplayStartRequested()
        {
            if (!EditorApplication.isPlaying)
            {
                return;
            }

            string inputPath = InputRecordingFileHelper.ResolveLatestRecording("");
            if (string.IsNullOrEmpty(inputPath))
            {
                Debug.LogWarning("[VerificationBridge] No recording file found");
                return;
            }

            InputRecordingData? data = InputRecordingFileHelper.Load(inputPath);
            if (data == null)
            {
                Debug.LogWarning($"[VerificationBridge] Failed to load recording: {inputPath}");
                return;
            }

            OverlayCanvasFactory.EnsureExists();
            RecordReplayOverlayFactory.EnsureReplayOverlay();
            InputReplayer.StartReplay(data, loop: false, showOverlay: true);

            int eventCount = data.GetTotalEventCount();
            Debug.Log($"[VerificationBridge] Replay started: {eventCount} events, {data.Metadata.TotalFrames} frames from {inputPath}");
        }

        private static void OnReplayStopRequested()
        {
            if (!InputReplayer.IsReplaying)
            {
                return;
            }

            InputReplayer.StopReplay();
            Debug.Log("[VerificationBridge] Replay stopped by user");
        }

        private static void OnReplayCompleted()
        {
            Debug.Log("[VerificationBridge] Replay completed, running auto-verification");

            InputReplayVerificationController? controller = Object.FindObjectOfType<InputReplayVerificationController>();
            if (controller == null)
            {
                return;
            }

            controller.OnReplayCompleted();
            controller.OnSaveReplayLog();
            controller.OnCompareLogs();
        }
    }
}
#endif
