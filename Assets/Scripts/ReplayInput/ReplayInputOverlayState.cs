#nullable enable

namespace io.github.hatayama.uLoopMCP
{
    public static class ReplayInputOverlayState
    {
        private static bool _isActive;
        private static int _currentFrame;
        private static int _totalFrames;
        private static bool _isLooping;

        public static bool IsActive => _isActive;
        public static int CurrentFrame => _currentFrame;
        public static int TotalFrames => _totalFrames;
        public static bool IsLooping => _isLooping;

        public static float Progress
        {
            get
            {
                return _totalFrames > 0 ? (float)_currentFrame / _totalFrames : 0f;
            }
        }

        public static void Update(int currentFrame, int totalFrames, bool isLooping)
        {
            _isActive = true;
            _currentFrame = currentFrame;
            _totalFrames = totalFrames;
            _isLooping = isLooping;
        }

        public static void Clear()
        {
            _isActive = false;
            _currentFrame = 0;
            _totalFrames = 0;
            _isLooping = false;
        }
    }
}
