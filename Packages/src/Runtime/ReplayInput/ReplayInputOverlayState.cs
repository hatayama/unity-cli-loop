#nullable enable

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides Replay Input Overlay State operations for its owning module.
    /// </summary>
    public sealed class ReplayInputOverlayStateService
    {
        private bool _isActive;
        private int _currentFrame;
        private int _totalFrames;
        private bool _isLooping;

        public bool IsActive => _isActive;
        public int CurrentFrame => _currentFrame;
        public int TotalFrames => _totalFrames;
        public bool IsLooping => _isLooping;

        public float Progress
        {
            get
            {
                return _totalFrames > 0 ? (float)_currentFrame / _totalFrames : 0f;
            }
        }

        public void Update(int currentFrame, int totalFrames, bool isLooping)
        {
            _isActive = true;
            _currentFrame = currentFrame;
            _totalFrames = totalFrames;
            _isLooping = isLooping;
        }

        public void Clear()
        {
            _isActive = false;
            _currentFrame = 0;
            _totalFrames = 0;
            _isLooping = false;
        }
    }

    /// <summary>
    /// Stores Replay Input Overlay state shared by the owning workflow.
    /// </summary>
    public static class ReplayInputOverlayState
    {
        private static readonly ReplayInputOverlayStateService ServiceValue = new ReplayInputOverlayStateService();

        public static bool IsActive => ServiceValue.IsActive;
        public static int CurrentFrame => ServiceValue.CurrentFrame;
        public static int TotalFrames => ServiceValue.TotalFrames;
        public static bool IsLooping => ServiceValue.IsLooping;
        public static float Progress => ServiceValue.Progress;

        public static void Update(int currentFrame, int totalFrames, bool isLooping)
        {
            ServiceValue.Update(currentFrame, totalFrames, isLooping);
        }

        public static void Clear()
        {
            ServiceValue.Clear();
        }
    }
}
