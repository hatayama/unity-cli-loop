using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Marks source locations that the Unity CLI can arm as temporary Editor pause points.
    /// </summary>
    public static class UloopPausePoint
    {
        /// <summary>
        /// Records a named pause point hit when the Editor has armed the same id.
        /// </summary>
        [Conditional("UNITY_EDITOR")]
        public static void Hit(string id)
        {
#if UNITY_EDITOR
            UloopPausePointRegistry.Hit(id);
#endif
        }
    }
}
