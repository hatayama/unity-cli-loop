#if UNITY_EDITOR
namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Centralizes captured-variable scope names shared by Editor tools and the native CLI.
    /// </summary>
    internal static class UloopCapturedVariableScope
    {
        public const string Local = "Local";
        public const string Parameter = "Parameter";
        public const string InstanceField = "InstanceField";
        public const string This = "This";
    }
}
#endif
