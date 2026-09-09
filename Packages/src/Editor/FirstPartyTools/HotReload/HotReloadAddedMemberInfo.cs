using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// One added-method shim recorded for --status Kind "Added".
    /// </summary>
    internal sealed class HotReloadAddedMemberInfo
    {
        public string MethodKey { get; }

        public string FilePath { get; }

        public MethodInfo ShimMethod { get; }

        public HotReloadAddedMemberInfo(string methodKey, string filePath, MethodInfo shimMethod)
        {
            MethodKey = methodKey ?? string.Empty;
            FilePath = filePath ?? string.Empty;
            ShimMethod = shimMethod;
        }
    }
}
