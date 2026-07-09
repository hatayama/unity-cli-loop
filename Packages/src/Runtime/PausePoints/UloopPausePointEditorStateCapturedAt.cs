#if UNITY_EDITOR
namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Names when a pause point editor-state snapshot was captured.
    /// </summary>
    internal static class UloopPausePointEditorStateCapturedAt
    {
        public const string Current = "Current";
        public const string PausePointHit = "PausePointHit";
        public const string ClearAll = "ClearAll";
    }
}
#endif
