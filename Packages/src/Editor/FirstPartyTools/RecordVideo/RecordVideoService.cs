using System;
using System.IO;
using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Static facade for the Editor-resident video recording session.
    /// </summary>
    internal static class RecordVideoService
    {
        private static VideoRecordingSession _session;
        private static bool _usedDefaultOutputPath;

        internal static bool IsRecording => _session != null && _session.Snapshot().IsRecording;

        internal static void InitializeForEditorStartup()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += OnBeforeAssemblyReload;
            EditorApplication.quitting -= OnEditorQuitting;
            EditorApplication.quitting += OnEditorQuitting;
        }

        internal static VideoRecordingSnapshot Start(
            int frameRate,
            int maxDurationSeconds,
            string outputPath,
            bool usedDefaultOutputPath,
            int width,
            int height,
            float resolutionScale,
            RecordVideoQuality quality)
        {
            Debug.Assert(!IsRecording, "Start must not run while a recording is already active.");
            Debug.Assert(!string.IsNullOrEmpty(outputPath), "outputPath must not be empty.");
            Debug.Assert(width > 0, "encoder width must be a positive even size.");
            Debug.Assert(height > 0, "encoder height must be a positive even size.");
            Debug.Assert((width & 1) == 0, "encoder width must be even.");
            Debug.Assert((height & 1) == 0, "encoder height must be even.");

            PlayModeViewFrameSource frameSource = new PlayModeViewFrameSource(resolutionScale);

            string directory = Path.GetDirectoryName(outputPath);
            Debug.Assert(!string.IsNullOrEmpty(directory), "outputPath must include a directory.");
            Directory.CreateDirectory(directory);

            bool useVp8 = string.Equals(
                Path.GetExtension(outputPath),
                RecordVideoConstants.WebmExtension,
                StringComparison.OrdinalIgnoreCase);
            MediaEncoderVideoFrameEncoder encoder = new MediaEncoderVideoFrameEncoder(
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
                    () => EditorApplication.timeSinceStartup,
                    frameRate,
                    maxDurationSeconds,
                    outputPath,
                    quality);
                _usedDefaultOutputPath = usedDefaultOutputPath;
                LastCompletedRecordingStore.Clear();
                EditorApplication.update -= OnEditorUpdate;
                EditorApplication.update += OnEditorUpdate;
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

        internal static VideoRecordingSnapshot Stop(string reason)
        {
            if (_session == null)
            {
                return default;
            }

            _session.Stop(reason);
            return FinishStoppedSession(reason);
        }

        internal static VideoRecordingSnapshot GetSnapshot()
        {
            if (_session == null)
            {
                return default;
            }

            return _session.Snapshot();
        }

        private static void OnEditorUpdate()
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

        private static VideoRecordingSnapshot FinishStoppedSession(string reason)
        {
            EditorApplication.update -= OnEditorUpdate;
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
                    ApplyDefaultDirectoryRetention();
                }

                return snapshot;
            }
            finally
            {
                _session = null;
                _usedDefaultOutputPath = false;
            }
        }

        private static void ApplyDefaultDirectoryRetention()
        {
            string directory = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                UnityCliLoopConstants.OUTPUT_ROOT_DIR,
                UnityCliLoopConstants.VIDEOS_DIR);
            OutputFileRetention.DeleteOldestBeyondLimit(directory, RecordVideoConstants.Mp4SearchPattern);
            OutputFileRetention.DeleteOldestBeyondLimit(directory, RecordVideoConstants.WebmSearchPattern);
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingPlayMode)
            {
                return;
            }

            if (_session == null)
            {
                return;
            }

            Stop(RecordVideoConstants.StoppedByPlayModeExit);
        }

        private static void OnBeforeAssemblyReload()
        {
            if (_session == null)
            {
                return;
            }

            Stop(RecordVideoConstants.StoppedByAssemblyReload);
        }

        private static void OnEditorQuitting()
        {
            if (_session == null)
            {
                return;
            }

            Stop(RecordVideoConstants.StoppedByEditorQuit);
        }
    }
}
