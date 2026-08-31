#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Input Recorder operations for its owning module.
    /// </summary>
    internal sealed class InputRecorderService
    {
        private readonly InputRecorderFrameCapture _frameCapture = new InputRecorderFrameCapture();
        private readonly List<RecordedInputEvent> _frameEvents = new();

        private bool _isRecording;
        private int _startFrameCount;
        private float _startTime;
        private List<InputFrameEvents> _recordedFrames = new();

        public event Action? RecordingStarted;
        public event Action? RecordingStopped;

        public bool IsRecording => _isRecording;

        public void Initialize()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public void StartRecording(HashSet<Key>? keyFilter)
        {
            Debug.Assert(!_isRecording, "Cannot start recording while already recording");
            Debug.Assert(EditorApplication.isPlaying, "PlayMode must be active to start recording");

            _recordedFrames = new List<InputFrameEvents>();
            _frameCapture.BeginCapture(keyFilter);
            _startFrameCount = Time.frameCount;
            _startTime = Time.realtimeSinceStartup;
            _isRecording = true;

            // Recording captures real user input; clear simulate-mouse-input overlay
            // so it doesn't linger from a previous tool call.
            SimulateMouseInputOverlayState.Clear();

            EmitInitialHeldEvents();

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            InputSystem.onAfterUpdate += OnAfterUpdate;

            RecordingStarted?.Invoke();
        }

        public InputRecordingData StopRecording()
        {
            Debug.Assert(_isRecording, "Cannot stop recording when not recording");

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            _isRecording = false;
            RecordInputOverlayState.Clear();

            int totalFrames = Time.frameCount - _startFrameCount;
            float duration = Time.realtimeSinceStartup - _startTime;

            InputRecordingData data = new()
            {
                Metadata = new InputRecordingMetadata
                {
                    RecordedAt = DateTime.UtcNow.ToString("o"),
                    TotalFrames = totalFrames,
                    DurationSeconds = duration
                },
                Frames = _recordedFrames
            };

            Reset();
            return data;
        }

        // Call after the recording data has been saved to disk,
        // so subscribers (e.g. RecordingsEditorWindow) see the new file
        public void NotifyRecordingStopped()
        {
            RecordingStopped?.Invoke();
        }

        public void ForceStop()
        {
            if (!_isRecording)
            {
                return;
            }

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            _isRecording = false;
            RecordInputOverlayState.Clear();
            Reset();
            RecordingStopped?.Invoke();
        }

        private void Reset()
        {
            _recordedFrames = new List<InputFrameEvents>();
            _frameCapture.Reset();
        }

        private void OnAfterUpdate()
        {
            if (!_isRecording)
            {
                return;
            }

            InputUpdateType currentUpdateType = InputState.currentUpdateType;
            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();
            if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
            {
                return;
            }

            float elapsed = Time.realtimeSinceStartup - _startTime;
            if (elapsed > RecordInputConstants.MAX_RECORDING_DURATION_SECONDS)
            {
                InputRecordingData data = StopRecording();
                string outputPath = InputRecordingFileHelper.ResolveOutputPath("");
                InputRecordingFileHelper.Save(data, outputPath);
                NotifyRecordingStopped();
                Debug.LogWarning($"[InputRecorder] Recording auto-stopped after {RecordInputConstants.MAX_RECORDING_DURATION_SECONDS}s limit. Saved to {outputPath}");
                return;
            }

            int relativeFrame = Time.frameCount - _startFrameCount;
            _frameEvents.Clear();

            _frameCapture.CaptureFrameEvents(_frameEvents);

            if (_frameEvents.Count > 0)
            {
                List<RecordedInputEvent> snapshot = new(_frameEvents);
                _recordedFrames.Add(new InputFrameEvents
                {
                    Frame = relativeFrame,
                    Events = snapshot
                });
            }
        }

        // Keys/buttons already held when recording starts need explicit DOWN events,
        // otherwise replay starts with those controls released until a state change occurs.
        private void EmitInitialHeldEvents()
        {
            List<RecordedInputEvent> events = _frameCapture.BuildInitialHeldEvents();
            if (events.Count > 0)
            {
                _recordedFrames.Add(new InputFrameEvents
                {
                    Frame = 0,
                    Events = events
                });
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                ForceStop();
            }
        }
    }

    /// <summary>
    /// Provides Input Recorder behavior for Unity CLI Loop.
    /// </summary>
    internal static class InputRecorder
    {
        private static readonly InputRecorderService ServiceValue = new InputRecorderService();

        internal static void InitializeForEditorStartup()
        {
            ServiceValue.Initialize();
        }

        public static void AddRecordingStartedHandler(Action handler)
        {
            ServiceValue.RecordingStarted += handler;
        }

        public static void RemoveRecordingStartedHandler(Action handler)
        {
            ServiceValue.RecordingStarted -= handler;
        }

        public static void AddRecordingStoppedHandler(Action handler)
        {
            ServiceValue.RecordingStopped += handler;
        }

        public static void RemoveRecordingStoppedHandler(Action handler)
        {
            ServiceValue.RecordingStopped -= handler;
        }

        public static bool IsRecording => ServiceValue.IsRecording;

        public static void StartRecording(HashSet<Key>? keyFilter)
        {
            ServiceValue.StartRecording(keyFilter);
        }

        public static InputRecordingData StopRecording()
        {
            return ServiceValue.StopRecording();
        }

        public static void NotifyRecordingStopped()
        {
            ServiceValue.NotifyRecordingStopped();
        }

        internal static string FormatVector2(Vector2 v)
        {
            return InputRecordingVectorFormat.FormatVector2(v);
        }

        internal static Vector2 ParseVector2(string data)
        {
            return InputRecordingVectorFormat.ParseVector2(data);
        }
    }
}
#endif
