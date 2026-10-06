namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Stores the latest Unity play and compile state for responses created outside the main thread.
    /// Infrastructure keeps this cache fresh by subscribing to Editor update/play-mode events and calling
    /// <see cref="SetPlayState"/> and <see cref="SetCompileState"/>; this class holds no Editor platform
    /// dependency itself.
    /// </summary>
    internal static class UnityCliLoopEditorStateSnapshot
    {
        private static readonly object StateLock = new();
        private static bool _hasPlayState;
        private static bool _isPlaying;
        private static bool _isPaused;
        private static bool _hasCompileState;
        private static bool _isCompiling;
        private static bool _isUpdating;

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

        // Why: Infrastructure's Editor update/play-mode subscriber is the only production caller;
        // internal (not private) so it can refresh this cache without Application depending on the Editor platform.
        internal static void SetPlayState(bool isPlaying, bool isPaused)
        {
            lock (StateLock)
            {
                _hasPlayState = true;
                _isPlaying = isPlaying;
                _isPaused = isPaused;
            }
        }

        internal static void SetPlayStateForTesting(bool isPlaying, bool isPaused)
        {
            SetPlayState(isPlaying, isPaused);
        }

        internal static (bool HasValue, bool IsCompiling, bool IsUpdating) GetCompileState()
        {
            lock (StateLock)
            {
                return (
                    HasValue: _hasCompileState,
                    IsCompiling: _isCompiling,
                    IsUpdating: _isUpdating);
            }
        }

        // Why: same single production caller as SetPlayState, for the same reason.
        internal static void SetCompileState(bool isCompiling, bool isUpdating)
        {
            lock (StateLock)
            {
                _hasCompileState = true;
                _isCompiling = isCompiling;
                _isUpdating = isUpdating;
            }
        }

        internal static void SetCompileStateForTesting(bool isCompiling, bool isUpdating)
        {
            SetCompileState(isCompiling, isUpdating);
        }

        internal static void ClearForTesting()
        {
            lock (StateLock)
            {
                _hasPlayState = false;
                _isPlaying = false;
                _isPaused = false;
                _hasCompileState = false;
                _isCompiling = false;
                _isUpdating = false;
            }
        }
    }
}
