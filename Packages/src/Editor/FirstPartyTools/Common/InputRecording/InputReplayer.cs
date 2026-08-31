#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Provides Input Replayer operations for its owning module.
    /// </summary>
    internal sealed class InputReplayerService
    {
        private readonly InputReplayEventProcessor _eventProcessor = new InputReplayEventProcessor();
        private readonly InputReplayUiController _uiController;

        private bool _isReplaying;
        private InputRecordingData? _data;
        private int _eventIndex;
        private int _currentFrame;
        private bool _loop;
        private bool _showOverlay;

        public InputReplayerService()
        {
            _uiController = new InputReplayUiController(_eventProcessor);
        }

        public event Action? ReplayStarted;
        public event Action? ReplayCompleted;

        public bool IsReplaying => _isReplaying;
        public int CurrentFrame => _currentFrame;
        public int TotalFrames => _data?.Metadata.TotalFrames ?? 0;

        public float Progress
        {
            get
            {
                int total = TotalFrames;
                return total > 0 ? (float)_currentFrame / total : 0f;
            }
        }

        public void RegisterPlayModeCallbacks()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        public void StartReplay(InputRecordingData data, bool loop, bool showOverlay)
        {
            Debug.Assert(!_isReplaying, "Cannot start replay while already replaying");
            Debug.Assert(EditorApplication.isPlaying, "PlayMode must be active to start replay");
            Debug.Assert(data != null, "Recording data must not be null");

            _data = data;
            _eventIndex = 0;
            _currentFrame = 0;
            _loop = loop;
            _showOverlay = showOverlay;
            if (_showOverlay)
            {
                RecordReplayOverlayFactory.EnsureReplayOverlay();
            }

            _eventProcessor.InitializeForRecording(data!);
            _uiController.Reset();
            if (_eventProcessor.HasMousePosition)
            {
                SimulateMouseInputOverlayState.Clear();
            }

            _isReplaying = true;

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            InputSystem.onAfterUpdate += OnAfterUpdate;

            ReplayStarted?.Invoke();
        }

        public void StopReplay()
        {
            if (!_isReplaying)
            {
                return;
            }

            InputSystem.onAfterUpdate -= OnAfterUpdate;
            _isReplaying = false;

            _eventProcessor.ReleaseAllHeldInputs();

            _data = null;
            _eventIndex = 0;
            _currentFrame = 0;
            _eventProcessor.ClearHeldState();
            _uiController.RestoreUiInputModules();
            _uiController.Reset();

            ReplayInputOverlayState.Clear();
            if (_eventProcessor.HasMousePosition)
            {
                SimulateMouseUiOverlayState.Clear();
            }
        }

        private void OnAfterUpdate()
        {
            if (!_isReplaying || _data == null)
            {
                return;
            }

            InputUpdateType currentUpdateType = InputState.currentUpdateType;
            InputUpdateType targetUpdateType = InputUpdateTypeResolver.Resolve();
            if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, targetUpdateType))
            {
                return;
            }

            Vector2 frameDelta = Vector2.zero;
            Vector2 frameScroll = Vector2.zero;

            CollectFrameState(ref frameDelta, ref frameScroll);
            _eventProcessor.ApplyCurrentFrameSnapshot(Keyboard.current, Mouse.current, frameDelta, frameScroll);

            if (_eventProcessor.HasMousePosition)
            {
                _uiController.ApplyUiEvents();
            }

            if (_showOverlay)
            {
                ReplayInputOverlayState.Update(_currentFrame, _data.Metadata.TotalFrames, _loop);
            }

            _currentFrame++;

            if (_eventIndex >= _data.Frames.Count && _currentFrame > _data.Metadata.TotalFrames)
            {
                if (_loop)
                {
                    _eventProcessor.ReleaseAllHeldInputs();
                    _eventIndex = 0;
                    _currentFrame = 0;
                    _uiController.Reset();
                }
                else
                {
                    StopReplay();
                    ReplayCompleted?.Invoke();
                }
            }
        }

        private void CollectFrameState(ref Vector2 frameDelta, ref Vector2 frameScroll)
        {
            Debug.Assert(_data != null, "_data must not be null while replaying");

            while (_eventIndex < _data!.Frames.Count && _data.Frames[_eventIndex].Frame <= _currentFrame)
            {
                InputFrameEvents frameEvents = _data.Frames[_eventIndex];
                for (int i = 0; i < frameEvents.Events.Count; i++)
                {
                    _eventProcessor.ProcessEvent(frameEvents.Events[i], ref frameDelta, ref frameScroll);
                }

                _eventIndex++;
            }
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.ExitingPlayMode)
            {
                StopReplay();
            }
        }
    }

    /// <summary>
    /// Provides Input Replayer behavior for Unity CLI Loop.
    /// </summary>
    internal static class InputReplayer
    {
        private static readonly InputReplayerService ServiceValue = new InputReplayerService();

        internal static void InitializeForEditorStartup()
        {
            ServiceValue.RegisterPlayModeCallbacks();
        }

        public static void AddReplayStartedHandler(Action handler)
        {
            ServiceValue.ReplayStarted += handler;
        }

        public static void RemoveReplayStartedHandler(Action handler)
        {
            ServiceValue.ReplayStarted -= handler;
        }

        public static void AddReplayCompletedHandler(Action handler)
        {
            ServiceValue.ReplayCompleted += handler;
        }

        public static void RemoveReplayCompletedHandler(Action handler)
        {
            ServiceValue.ReplayCompleted -= handler;
        }

        public static bool IsReplaying => ServiceValue.IsReplaying;
        public static int CurrentFrame => ServiceValue.CurrentFrame;
        public static int TotalFrames => ServiceValue.TotalFrames;
        public static float Progress => ServiceValue.Progress;

        public static void StartReplay(InputRecordingData data, bool loop, bool showOverlay)
        {
            ServiceValue.StartReplay(data, loop, showOverlay);
        }

        public static void StopReplay()
        {
            ServiceValue.StopReplay();
        }
    }
}
#endif
