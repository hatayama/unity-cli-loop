using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Stores the latest Unity play state for error responses created outside the main thread.
    /// </summary>
    internal static class UnityCliLoopEditorStateSnapshot
    {
        private static readonly object StateLock = new();
        private static bool _hasPlayState;
        private static bool _isPlaying;
        private static bool _isPaused;

        internal static void InitializeForEditorStartup()
        {
            EditorApplication.update -= RefreshFromEditor;
            EditorApplication.update += RefreshFromEditor;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            RefreshFromEditor();
        }

        internal static (bool HasValue, bool IsPlaying, bool IsPaused) GetPlayState()
        {
            lock (StateLock)
            {
                return (
                    HasValue: _hasPlayState,
                    IsPlaying: _isPlaying,
                    IsPaused: _isPaused);
            }
        }

        internal static void SetPlayStateForTesting(bool isPlaying, bool isPaused)
        {
            SetPlayState(isPlaying, isPaused);
        }

        internal static void ClearForTesting()
        {
            lock (StateLock)
            {
                _hasPlayState = false;
                _isPlaying = false;
                _isPaused = false;
            }
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            RefreshFromEditor();
        }

        private static void RefreshFromEditor()
        {
            SetPlayState(
                EditorApplication.isPlaying,
                EditorApplication.isPaused);
        }

        private static void SetPlayState(bool isPlaying, bool isPaused)
        {
            lock (StateLock)
            {
                _hasPlayState = true;
                _isPlaying = isPlaying;
                _isPaused = isPaused;
            }
        }
    }
}
