using System.IO;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Static facade for the Editor-resident video recording session.
    /// </summary>
    internal static class RecordVideoService
    {
        private static readonly RecordVideoSessionHost ServiceValue = new RecordVideoSessionHost(
            (outputPath, width, height, frameRate, useVp8, quality) => new MediaEncoderVideoFrameEncoder(
                outputPath,
                width,
                height,
                frameRate,
                useVp8,
                quality),
            () => EditorApplication.timeSinceStartup,
            callback => EditorApplication.update += callback,
            callback => EditorApplication.update -= callback,
            ApplyDefaultDirectoryRetention);

        internal static bool IsRecording => ServiceValue.IsRecording;

        internal static void InitializeForEditorStartup()
        {
            EditorApplication.playModeStateChanged -= ServiceValue.OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += ServiceValue.OnPlayModeStateChanged;
            AssemblyReloadEvents.beforeAssemblyReload -= ServiceValue.OnBeforeAssemblyReload;
            AssemblyReloadEvents.beforeAssemblyReload += ServiceValue.OnBeforeAssemblyReload;
            EditorApplication.quitting -= ServiceValue.OnEditorQuitting;
            EditorApplication.quitting += ServiceValue.OnEditorQuitting;
        }

        internal static VideoRecordingSnapshot Start(
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
            return ServiceValue.Start(
                frameRate,
                maxDurationSeconds,
                outputPath,
                usedDefaultOutputPath,
                width,
                height,
                frameSource,
                stopOnPlayModeExit,
                quality);
        }

        internal static VideoRecordingSnapshot Stop(string reason)
        {
            return ServiceValue.Stop(reason);
        }

        internal static VideoRecordingSnapshot GetSnapshot()
        {
            return ServiceValue.GetSnapshot();
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
    }
}
