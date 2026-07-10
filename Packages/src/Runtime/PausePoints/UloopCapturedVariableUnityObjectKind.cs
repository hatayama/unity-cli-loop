#if UNITY_EDITOR
namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// Centralizes the UnityEngine.Object classification names shared by Editor tools and the
    /// native CLI. A real C# null reference has no kind (empty string) rather than one of these.
    /// </summary>
    internal static class UloopCapturedVariableUnityObjectKind
    {
        public const string SceneObject = "SceneObject";
        public const string PrefabAsset = "PrefabAsset";
        public const string Asset = "Asset";
        public const string RuntimeInstance = "RuntimeInstance";
        public const string Destroyed = "Destroyed";
    }
}
#endif
