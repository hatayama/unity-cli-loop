using System;
using System.IO;
using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Owns the single active video recording and its Editor lifecycle reactions.
    /// The encoder, clock, update subscription, and default-folder retention are injected
    /// so the lifecycle can be tested without encoding video or registering real Editor callbacks.
    /// </summary>
    internal sealed class RecordVideoSessionHost
    {
        private readonly Func<string, int, int, int, bool, RecordVideoQuality, IVideoFrameEncoder> _createEncoder;
        private readonly Func<double> _clock;
        private readonly Action<EditorApplication.CallbackFunction> _subscribeUpdate;
        private readonly Action<EditorApplication.CallbackFunction> _unsubscribeUpdate;
        private readonly Action _applyDefaultDirectoryRetention;
        private VideoRecordingSession _session;
        private bool _usedDefaultOutputPath;
        private bool _stopOnPlayModeExit;

        internal RecordVideoSessionHost(
            Func<string, int, int, int, bool, RecordVideoQuality, IVideoFrameEncoder> createEncoder,
            Func<double> clock,
            Action<EditorApplication.CallbackFunction> subscribeUpdate,
            Action<EditorApplication.CallbackFunction> unsubscribeUpdate,
            Action applyDefaultDirectoryRetention)
        {
            Debug.Assert(createEncoder != null, "createEncoder must not be null");
            Debug.Assert(clock != null, "clock must not be null");
            Debug.Assert(subscribeUpdate != null, "subscribeUpdate must not be null");
            Debug.Assert(unsubscribeUpdate != null, "unsubscribeUpdate must not be null");
            Debug.Assert(applyDefaultDirectoryRetention != null, "applyDefaultDirectoryRetention must not be null");

            _createEncoder = createEncoder;
            _clock = clock;
            _subscribeUpdate = subscribeUpdate;
            _unsubscribeUpdate = unsubscribeUpdate;
            _applyDefaultDirectoryRetention = applyDefaultDirectoryRetention;
        }

        internal bool IsRecording => _session != null && _session.Snapshot().IsRecording;

        internal VideoRecordingSnapshot Start(
            int frameRate,
            int maxDurationSeconds,
            string outputPath,
            bool usedDefaultOutputPath,
            int width,
            int height,
            IGameViewFrameSource frameSource,
            bool stopOnPlayModeExit,
            RecordVideoQuality quality)
        {
            Debug.Assert(!IsRecording, "Start must not run while a recording is already active.");
            Debug.Assert(!string.IsNullOrEmpty(outputPath), "outputPath must not be empty.");
            Debug.Assert(width > 0, "encoder width must be a positive even size.");
            Debug.Assert(height > 0, "encoder height must be a positive even size.");
            Debug.Assert((width & 1) == 0, "encoder width must be even.");
            Debug.Assert((height & 1) == 0, "encoder height must be even.");

            string directory = Path.GetDirectoryName(outputPath);
            Debug.Assert(!string.IsNullOrEmpty(directory), "outputPath must include a directory.");
            Directory.CreateDirectory(directory);

            bool useVp8 = string.Equals(
                Path.GetExtension(outputPath),
                RecordVideoConstants.WebmExtension,
                StringComparison.OrdinalIgnoreCase);
            IVideoFrameEncoder encoder = _createEncoder(
                outputPath,
                width,
                height,
                frameRate,
                useVp8,
                quality);
            try
            {
                _session = new VideoRecordingSession(
                    encoder,
                    frameSource,
                    _clock,
                    frameRate,
                    maxDurationSeconds,
                    outputPath,
                    quality);
                _usedDefaultOutputPath = usedDefaultOutputPath;
                _stopOnPlayModeExit = stopOnPlayModeExit;
                LastCompletedRecordingStore.Clear();
                _unsubscribeUpdate(OnEditorUpdate);
                _subscribeUpdate(OnEditorUpdate);
                return _session.Snapshot();
            }
            finally
            {
                if (_session == null)
                {
                    encoder.Dispose();
                }
            }
        }

        internal VideoRecordingSnapshot Stop(string reason)
        {
            if (_session == null)
            {
                return default;
            }

            _session.Stop(reason);
            return FinishStoppedSession(reason);
        }

        internal VideoRecordingSnapshot GetSnapshot()
        {
            if (_session == null)
            {
                return default;
            }

            return _session.Snapshot();
        }

        internal void OnEditorUpdate()
        {
            if (_session == null)
            {
                return;
            }

            _session.Tick();
            if (_session.Snapshot().IsRecording)
            {
                return;
            }

            FinishStoppedSession(_session.Snapshot().StoppedBy);
        }

        private VideoRecordingSnapshot FinishStoppedSession(string reason)
        {
            _unsubscribeUpdate(OnEditorUpdate);
            VideoRecordingSnapshot snapshot = _session.Snapshot();
            try
            {
                // SessionState does not survive an Editor quit, so saving there on quit is a dead write.
                if (reason != RecordVideoConstants.StoppedByCli
                    && reason != RecordVideoConstants.StoppedByEditorQuit)
                {
                    LastCompletedRecordingStore.Save(snapshot);
                }

                if (_usedDefaultOutputPath)
                {
                    _applyDefaultDirectoryRetention();
                }

                return snapshot;
            }
            finally
            {
                _session = null;
                _usedDefaultOutputPath = false;
                _stopOnPlayModeExit = false;
            }
        }

        internal void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            if (_session == null)
            {
                return;
            }

            // A window recording is independent of Play Mode, so only a Game View recording
            // stops when Play Mode ends.
            if (!_stopOnPlayModeExit)
            {
                return;
            }

            Stop(RecordVideoConstants.StoppedByPlayModeExit);
        }

        internal void OnBeforeAssemblyReload()
        {
            if (_session == null)
            {
                return;
            }

            Stop(RecordVideoConstants.StoppedByAssemblyReload);
        }

        internal void OnEditorQuitting()
        {
            if (_session == null)
            {
                return;
            }

            Stop(RecordVideoConstants.StoppedByEditorQuit);
        }
    }
}
