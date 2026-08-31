#if UNITY_EDITOR
namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Machine-readable reasons a pause point transitioned to Cleared.
    /// Why: Cleared alone hid whether run-tests, explicit clear, or an expired marker was wiped.
    /// </summary>
    internal static class UloopPausePointClearedReason
    {
        public const string ExplicitClear = "ExplicitClear";
        public const string ClearAll = "ClearAll";
        public const string RunTestsAutoClear = "RunTestsAutoClear";
        public const string AfterExpired = "AfterExpired";
        public const string AlreadyHit = "AlreadyHit";
        public const string AwaitTimeoutAutoClear = "AwaitTimeoutAutoClear";
    }
}
#endif
