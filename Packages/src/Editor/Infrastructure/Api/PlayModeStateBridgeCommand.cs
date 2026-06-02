using UnityEditor;

namespace io.github.hatayama.UnityCliLoop.Infrastructure
{
    /// <summary>
    /// Reports PlayMode pause state for CLI-side Debug.Break waiting without entering the normal tool execution slot.
    /// </summary>
    internal static class PlayModeStateBridgeCommand
    {
        public static GetPlayModeStateResponse Execute()
        {
            return BuildResponse(EditorApplication.isPlaying, EditorApplication.isPaused);
        }

        internal static GetPlayModeStateResponse BuildResponse(bool isPlaying, bool isPaused)
        {
            return new GetPlayModeStateResponse
            {
                IsPlaying = isPlaying,
                IsPaused = isPaused,
                Message = CreateMessage(isPlaying, isPaused)
            };
        }

        private static string CreateMessage(bool isPlaying, bool isPaused)
        {
            if (isPaused)
            {
                return "Unity Editor is paused.";
            }

            if (isPlaying)
            {
                return "Unity Editor is playing and not paused.";
            }

            return "Unity Editor is not playing and not paused.";
        }
    }
}
