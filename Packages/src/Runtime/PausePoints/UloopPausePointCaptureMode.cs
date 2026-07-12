#if UNITY_EDITOR
namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Defines how a pause point behaves after it captures a hit.
    /// </summary>
    internal static class UloopPausePointCaptureMode
    {
        public const string SingleShot = "single-shot";
        public const string Continuous = "continuous";
        public const string Trace = "trace";
    }
}
#endif
