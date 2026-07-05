using UnityEditor;

using io.github.hatayama.UnityCliLoop.Application;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    // Owns the EditorApplication event subscriptions that used to live in Application, so Application
    // stays free of UnityEditor while UnityCliLoopEditorStateSnapshot keeps caching the latest play state.
    /// <summary>
    /// Keeps <see cref="UnityCliLoopEditorStateSnapshot"/> refreshed from Editor update and play-mode events.
    /// </summary>
    internal static class EditorRuntimeStateSnapshotSubscriber
    {
        internal static void InitializeForEditorStartup()
        {
            EditorApplication.update -= RefreshFromEditor;
            EditorApplication.update += RefreshFromEditor;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RefreshFromEditor();
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            RefreshFromEditor();
        }

        private static void RefreshFromEditor()
        {
            UnityCliLoopEditorStateSnapshot.SetPlayState(
                EditorApplication.isPlaying,
                EditorApplication.isPaused);
        }
    }
}
