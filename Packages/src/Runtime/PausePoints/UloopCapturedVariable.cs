#if UNITY_EDITOR
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.Runtime
{
    /// <summary>
    /// One formatted variable captured at a source pause point. Editor-only value types
    /// (UnityEngine.Object references, reflected field values) are reduced to string/int here so
    /// this DTO has no Editor API dependency and can be shared with the CLI bridge layer as-is.
    /// </summary>
    internal sealed class UloopCapturedVariable
    {
        public UloopCapturedVariable(
            string name,
            string scope,
            string typeName,
            string value,
            string unityObjectKind,
            string unityObjectPath,
            int unityObjectInstanceId)
        {
            Debug.Assert(!string.IsNullOrEmpty(name), "name must not be null or empty");
            Debug.Assert(!string.IsNullOrEmpty(scope), "scope must not be null or empty");

            Name = name;
            Scope = scope;
            TypeName = typeName ?? string.Empty;
            Value = value ?? string.Empty;
            UnityObjectKind = unityObjectKind ?? string.Empty;
            UnityObjectPath = unityObjectPath ?? string.Empty;
            UnityObjectInstanceId = unityObjectInstanceId;
        }

        public string Name { get; }
        public string Scope { get; }
        public string TypeName { get; }
        public string Value { get; }
        public string UnityObjectKind { get; }
        public string UnityObjectPath { get; }
        public int UnityObjectInstanceId { get; }
    }
}
#endif
