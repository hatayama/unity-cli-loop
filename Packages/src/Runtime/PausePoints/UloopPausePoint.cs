using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides the Editor-only pause point marker API for Unity CLI Loop workflows.
    /// </summary>
    public static class UloopPausePoint
    {
        /// <summary>
        /// Pauses at a named marker when the Editor has enabled the same id.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void Pause(string id)
        {
#if UNITY_EDITOR
            UloopPausePointRegistry.Hit(id);
#endif
        }
    }
}
