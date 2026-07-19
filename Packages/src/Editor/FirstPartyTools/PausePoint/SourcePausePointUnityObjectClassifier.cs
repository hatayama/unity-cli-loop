using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Classifies a live (non-null, non-destroyed) UnityEngine.Object reference into one of the
    /// pause-point capture kinds, with the handle (Hierarchy path / asset path / InstanceID) an
    /// AI needs in order to look the object up next.
    /// </summary>
    internal static class SourcePausePointUnityObjectClassifier
    {
        public readonly struct Classification
        {
            public Classification(string kind, string path, int instanceId)
            {
                Kind = kind;
                Path = path;
                InstanceId = instanceId;
            }

            public string Kind { get; }
            public string Path { get; }
            public int InstanceId { get; }
        }

        public static Classification Classify(Object unityObject)
        {
            Debug.Assert(unityObject != null, "unityObject must be a live (non-destroyed) reference.");

            if (unityObject is GameObject gameObject)
            {
                return ClassifyGameObjectOrComponent(gameObject, gameObject);
            }

            if (unityObject is Component component)
            {
                return ClassifyGameObjectOrComponent(component.gameObject, component);
            }

            string assetPath = AssetDatabase.GetAssetPath(unityObject);
            return string.IsNullOrEmpty(assetPath)
                ? new Classification(
                    UloopCapturedVariableUnityObjectKind.RuntimeInstance, unityObject.name, UnityObjectIdentifier.GetInstanceId(unityObject))
                : new Classification(
                    UloopCapturedVariableUnityObjectKind.Asset, assetPath, UnityObjectIdentifier.GetInstanceId(unityObject));
        }

        private static Classification ClassifyGameObjectOrComponent(GameObject gameObject, Object handleSource)
        {
            if (gameObject.scene.IsValid())
            {
                return new Classification(
                    UloopCapturedVariableUnityObjectKind.SceneObject,
                    $"{gameObject.scene.name}:{BuildHierarchyPath(gameObject.transform)}",
                    UnityObjectIdentifier.GetInstanceId(handleSource));
            }

            return new Classification(
                UloopCapturedVariableUnityObjectKind.PrefabAsset,
                AssetDatabase.GetAssetPath(handleSource),
                UnityObjectIdentifier.GetInstanceId(handleSource));
        }

        private static string BuildHierarchyPath(Transform transform)
        {
            return transform.parent == null
                ? "/" + transform.name
                : BuildHierarchyPath(transform.parent) + "/" + transform.name;
        }
    }
}
