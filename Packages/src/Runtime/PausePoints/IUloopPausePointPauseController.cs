#if UNITY_EDITOR
namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides the current pause state and performs the actual Unity Editor pause request.
    /// </summary>
    internal interface IUloopPausePointPauseController
    {
        bool IsPlaying { get; }
        bool IsPaused { get; }
        void Pause();
        void Resume();
    }
}
#endif
