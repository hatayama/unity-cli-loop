using System;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Names hosts and wired values by where they live - a place in a loaded scene, or an asset's
    /// GUID and local id - and finds them there again after a scene reload.
    /// </summary>
    internal sealed class HotReloadUnityWiredValueResolver : IHotReloadWiredValueResolver
    {
        private const string AssetPrefix = "asset:";
        private const string LocalMarker = "|local:";
        private const string ComponentMarker = "|component:";

        private readonly HotReloadSceneObjectIdentityBuilder _sceneObjects;

        internal HotReloadUnityWiredValueResolver(HotReloadSceneObjectIdentityBuilder sceneObjects)
        {
            Debug.Assert(sceneObjects != null, "sceneObjects must not be null.");
            _sceneObjects = sceneObjects;
        }

        // Scenes, transforms and the asset database answer on the main thread only.
        public bool IsMainThread => MainThreadSwitcher.IsMainThread;

        public bool IsPlayModeRunning => EditorApplication.isPlaying;

        public string DescribeHost(object host)
        {
            if (!IsMainThread || !(host is UnityEngine.Object unityObject) || unityObject == null)
            {
                return null;
            }

            if (EditorUtility.IsPersistent(unityObject))
            {
                return TryDescribeAsset(unityObject, out string assetIdentity) ? assetIdentity : null;
            }

            return host is Component component ? _sceneObjects.DescribeComponent(component) : null;
        }

        public bool IsHostMissing(string hostIdentity, bool unloadedSceneCountsAsMissing)
        {
            Debug.Assert(!string.IsNullOrEmpty(hostIdentity), "hostIdentity must not be empty.");

            // An asset host is not rebuilt by a scene reload, and off the main thread the scene
            // cannot be read, so neither is known to be missing.
            if (!IsMainThread || hostIdentity.StartsWith(AssetPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            return _sceneObjects.IsComponentMissing(hostIdentity, unloadedSceneCountsAsMissing);
        }

        public bool TryResolveHost(string hostIdentity, out object host)
        {
            Debug.Assert(!string.IsNullOrEmpty(hostIdentity), "hostIdentity must not be empty.");

            host = null;
            // An asset host is not rebuilt by a scene reload, so there is nothing to find again.
            if (!IsMainThread || hostIdentity.StartsWith(AssetPrefix, StringComparison.Ordinal))
            {
                return false;
            }

            if (!_sceneObjects.TryResolveComponent(hostIdentity, out Component component) || component == null)
            {
                return false;
            }

            host = component;
            return true;
        }

        public HotReloadWiredValueDescriptor DescribeValue(object value)
        {
            if (!(value is UnityEngine.Object unityObject))
            {
                return HotReloadWiredValueDescriptor.Plain(value);
            }

            string typeName = unityObject.GetType().FullName;
            // A destroyed object reads as null through Unity's own operator, and null is what the
            // field would have given back to its reader, so that is what is remembered.
            if (unityObject == null)
            {
                return HotReloadWiredValueDescriptor.Plain(null);
            }

            if (EditorUtility.IsPersistent(unityObject))
            {
                return TryDescribeAsset(unityObject, out string assetIdentity)
                    ? HotReloadWiredValueDescriptor.Asset(assetIdentity, typeName)
                    : HotReloadWiredValueDescriptor.Unrestorable(typeName, "the asset has no GUID in the asset database; wire the field again");
            }

            string sceneIdentity = DescribeSceneObject(unityObject);
            if (sceneIdentity != null)
            {
                return HotReloadWiredValueDescriptor.SceneObject(sceneIdentity, typeName);
            }

            return HotReloadWiredValueDescriptor.Unrestorable(
                typeName, "the value is a runtime-created object that no scene or asset holds; wire the field again "
                + "after each scene reload");
        }

        public bool TryResolve(HotReloadWiredValueDescriptor descriptor, out object value, out string failureReason)
        {
            Debug.Assert(descriptor != null, "descriptor must not be null.");

            value = null;
            failureReason = null;
            if (!IsMainThread)
            {
                failureReason = "it was read off the main thread, where scenes and assets cannot be searched; "
                    + "wire the field again from the main thread";
                return false;
            }

            UnityEngine.Object found = descriptor.Kind == HotReloadWiredValueKind.Asset
                ? FindAsset(descriptor.Identity)
                : FindSceneObject(descriptor.Identity);
            if (found == null)
            {
                failureReason = "no object is at " + descriptor.Identity + " any more; wire the field again with a live object";
                return false;
            }

            value = found;
            return true;
        }

        private string DescribeSceneObject(UnityEngine.Object unityObject)
        {
            if (unityObject is Component component)
            {
                return _sceneObjects.DescribeComponent(component);
            }

            return unityObject is GameObject gameObject ? _sceneObjects.DescribeGameObject(gameObject) : null;
        }

        private UnityEngine.Object FindSceneObject(string identity)
        {
            if (identity.Contains(ComponentMarker))
            {
                return _sceneObjects.TryResolveComponent(identity, out Component component) ? component : null;
            }

            return _sceneObjects.TryResolveGameObject(identity, out GameObject gameObject) ? gameObject : null;
        }

        private static bool TryDescribeAsset(UnityEngine.Object asset, out string identity)
        {
            identity = null;
            if (!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(asset, out string guid, out long localId)
                || string.IsNullOrEmpty(guid))
            {
                return false;
            }

            identity = AssetPrefix + guid + LocalMarker + localId;
            return true;
        }

        private static UnityEngine.Object FindAsset(string identity)
        {
            int localStart = identity.IndexOf(LocalMarker, StringComparison.Ordinal);
            if (!identity.StartsWith(AssetPrefix, StringComparison.Ordinal) || localStart < 0)
            {
                return null;
            }

            string guid = identity.Substring(AssetPrefix.Length, localStart - AssetPrefix.Length);
            string assetPath = AssetDatabase.GUIDToAssetPath(guid);
            if (string.IsNullOrEmpty(assetPath))
            {
                return null;
            }

            // The main asset first: it is the usual wiring target, and loading it alone is cheap.
            UnityEngine.Object mainAsset = AssetDatabase.LoadMainAssetAtPath(assetPath);
            if (mainAsset != null && TryDescribeAsset(mainAsset, out string mainIdentity) && mainIdentity == identity)
            {
                return mainAsset;
            }

            foreach (UnityEngine.Object candidate in AssetDatabase.LoadAllAssetsAtPath(assetPath))
            {
                if (candidate != null && TryDescribeAsset(candidate, out string candidateIdentity) && candidateIdentity == identity)
                {
                    return candidate;
                }
            }

            return null;
        }
    }
}
