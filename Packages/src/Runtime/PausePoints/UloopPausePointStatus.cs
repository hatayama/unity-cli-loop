#if UNITY_EDITOR
namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Centralizes status names shared by Editor tools and the native CLI.
    /// </summary>
    internal static class UloopPausePointStatus
    {
        public const string NotEnabled = "NotEnabled";
        public const string Enabled = "Enabled";
        public const string Hit = "Hit";
        public const string Expired = "Expired";
        public const string Cleared = "Cleared";
    }
}
#endif
