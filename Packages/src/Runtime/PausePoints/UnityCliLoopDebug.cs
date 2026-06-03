using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Provides Editor-only debug helpers for Unity CLI Loop workflows.
    /// </summary>
    public static class UnityCliLoopDebug
    {
        /// <summary>
        /// Breaks at a named marker when the Editor has enabled the same id.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void Break(string id)
        {
#if UNITY_EDITOR
            UloopPausePointRegistry.Hit(id);
#endif
        }
    }
}
