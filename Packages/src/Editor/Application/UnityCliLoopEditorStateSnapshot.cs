namespace io.github.hatayama.UnityCliLoop.Application
{
    /// <summary>
    /// Stores the latest Unity play state for error responses created outside the main thread.
    /// Infrastructure keeps this cache fresh by subscribing to Editor update/play-mode events and calling
    /// <see cref="SetPlayState"/>; this class holds no Editor platform dependency itself.
    /// </summary>
    internal static class UnityCliLoopEditorStateSnapshot
    {
        private static readonly object StateLock = new();
        private static bool _hasPlayState;
        private static bool _isPlaying;
        private static bool _isPaused;

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

        internal static void ClearForTesting()
        {
            lock (StateLock)
            {
                _hasPlayState = false;
                _isPlaying = false;
                _isPaused = false;
            }
        }
    }
}
