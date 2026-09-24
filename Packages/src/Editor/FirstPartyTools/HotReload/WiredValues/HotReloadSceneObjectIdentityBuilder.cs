using System;
using System.Collections.Generic;

using UnityEngine;
using UnityEngine.SceneManagement;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Names a scene object by where it sits - scene, then each ancestor's name and sibling index,
    /// then component type and position - and finds the object at that place again. A scene
    /// reload rebuilds the same hierarchy, so the object it creates in place of a recorded one
    /// gets the same name.
    /// </summary>
    /// <remarks>
    /// Why not GlobalObjectId or an instance id: the instance id changes with every scene reload,
    /// and GlobalObjectId does not work for scene objects in play mode.
    /// Scene ids and object names are percent-encoded ('%', '|', '/', '[' become %25, %7C, %2F,
    /// %5B), so the markers and separators in an identity only ever come from this builder: a
    /// name that contains "|component:" cannot pass for a component, and the root "X[0]/Y" cannot
    /// pass for Y under X. Why not backslash escapes: "\|component:" still contains the marker,
    /// and every reader that searches for it would need to skip escaped occurrences.
    /// Resolution compares each level's whole encoded "Name[index]" token against the live
    /// hierarchy, so a name or index that differs never matches.
    /// </remarks>
    internal sealed class HotReloadSceneObjectIdentityBuilder
    {
        private const string ScenePrefix = "scene:";
        private const string PathMarker = "|path:";
        private const string ComponentMarker = "|component:";
        private const string IndexMarker = "|index:";
        private const char PathSeparator = '/';

        /// <summary>
        /// "scene:{path or name}|path:{Name[index]/...}", or null for an object in no valid scene
        /// (a prefab asset, for instance).
        /// </summary>
        internal string DescribeGameObject(GameObject gameObject)
        {
            Debug.Assert(gameObject != null, "gameObject must not be null.");

            Scene scene = gameObject.scene;
            // Why not isLoaded too: Awake on a reloaded scene's objects runs while the scene is
            // still loading, and that is the first read a restore has to answer.
            if (!scene.IsValid())
            {
                return null;
            }

            List<string> tokens = new List<string>();
            for (Transform current = gameObject.transform; current != null; current = current.parent)
            {
                tokens.Add(Token(current));
            }

            tokens.Reverse();
            return ScenePrefix + SceneId(scene) + PathMarker + string.Join(PathSeparator.ToString(), tokens);
        }

        /// <summary>
        /// The game object's identity followed by "|component:{type}|index:{n}", where n counts only
        /// components of exactly that type on the object.
        /// </summary>
        internal string DescribeComponent(Component component)
        {
            Debug.Assert(component != null, "component must not be null.");

            string gameObjectIdentity = DescribeGameObject(component.gameObject);
            if (gameObjectIdentity == null)
            {
                return null;
            }

            Type type = component.GetType();
            int index = SameTypeComponents(component.gameObject, type.FullName).IndexOf(component);
            return gameObjectIdentity + ComponentMarker + type.FullName + IndexMarker + index;
        }

        internal bool TryResolveGameObject(string identity, out GameObject gameObject)
        {
            gameObject = null;
            if (!TrySplitSceneIdentity(identity, out string sceneId, out string path)
                || !TryFindLoadedScene(sceneId, out Scene scene))
            {
                return false;
            }

            List<Transform> roots = new List<Transform>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                roots.Add(root.transform);
            }

            Transform found = FindByPath(roots, path);
            gameObject = found != null ? found.gameObject : null;
            return gameObject != null;
        }

        internal bool TryResolveComponent(string identity, out Component component)
        {
            component = null;
            int componentStart = identity?.LastIndexOf(ComponentMarker, StringComparison.Ordinal) ?? -1;
            int indexStart = identity?.LastIndexOf(IndexMarker, StringComparison.Ordinal) ?? -1;
            if (componentStart < 0 || indexStart < componentStart)
            {
                return false;
            }

            string typeName = identity.Substring(
                componentStart + ComponentMarker.Length, indexStart - componentStart - ComponentMarker.Length);
            if (!int.TryParse(identity.Substring(indexStart + IndexMarker.Length), out int index)
                || !TryResolveGameObject(identity.Substring(0, componentStart), out GameObject gameObject))
            {
                return false;
            }

            List<Component> sameType = SameTypeComponents(gameObject, typeName);
            if (index < 0 || index >= sameType.Count)
            {
                return false;
            }

            component = sameType[index];
            return true;
        }

        /// <summary>
        /// Whether a component identity names a place in a readable scene where no component of
        /// that type sits any more. False when the scene cannot be read: the host is then out of
        /// sight, not gone, and is found again once that scene is open.
        /// </summary>
        internal bool IsComponentMissing(string identity)
        {
            if (!TrySplitSceneIdentity(identity, out string sceneId, out _)
                || !TryFindLoadedScene(sceneId, out _))
            {
                return false;
            }

            return !TryResolveComponent(identity, out _);
        }

        private static bool TrySplitSceneIdentity(string identity, out string sceneId, out string path)
        {
            sceneId = null;
            path = null;
            if (string.IsNullOrEmpty(identity) || !identity.StartsWith(ScenePrefix, StringComparison.Ordinal))
            {
                return false;
            }

            int pathStart = identity.IndexOf(PathMarker, StringComparison.Ordinal);
            if (pathStart < 0)
            {
                return false;
            }

            sceneId = identity.Substring(ScenePrefix.Length, pathStart - ScenePrefix.Length);
            path = identity.Substring(pathStart + PathMarker.Length);
            return true;
        }

        private static string Token(Transform transform)
        {
            return Encode(transform.name) + "[" + transform.GetSiblingIndex() + "]";
        }

        // '%' goes first so an encoded character is never encoded again.
        private static string Encode(string name)
        {
            return name
                .Replace("%", "%25")
                .Replace("|", "%7C")
                .Replace("/", "%2F")
                .Replace("[", "%5B");
        }

        // Why path before name: two open scenes can share a name, but not a path. An unsaved scene
        // has no path, so its name is all there is.
        private static string SceneId(Scene scene)
        {
            return Encode(string.IsNullOrEmpty(scene.path) ? scene.name : scene.path);
        }

        private static bool TryFindLoadedScene(string sceneId, out Scene found)
        {
            for (int i = 0; i < SceneManager.sceneCount; i++)
            {
                Scene scene = SceneManager.GetSceneAt(i);
                // Why not IsValid() alone: outside Play mode a scene that is still loading is valid
                // but GetRootGameObjects throws on it, and the read that asks for the restore is a
                // user callback (OnValidate, an ExecuteAlways Awake) that must not see that
                // exception. Such a scene counts as not there, so the restore is recorded as failed.
                bool readable = scene.IsValid() && (Application.isPlaying || scene.isLoaded);
                if (readable && string.Equals(SceneId(scene), sceneId, StringComparison.Ordinal))
                {
                    found = scene;
                    return true;
                }
            }

            found = default;
            return false;
        }

        private static Transform FindByPath(IReadOnlyList<Transform> candidates, string remainingPath)
        {
            foreach (Transform candidate in candidates)
            {
                string token = Token(candidate);
                if (!remainingPath.StartsWith(token, StringComparison.Ordinal))
                {
                    continue;
                }

                if (remainingPath.Length == token.Length)
                {
                    return candidate;
                }

                if (remainingPath[token.Length] != PathSeparator)
                {
                    continue;
                }

                Transform found = FindByPath(Children(candidate), remainingPath.Substring(token.Length + 1));
                if (found != null)
                {
                    return found;
                }
            }

            return null;
        }

        private static List<Transform> Children(Transform parent)
        {
            List<Transform> children = new List<Transform>(parent.childCount);
            for (int i = 0; i < parent.childCount; i++)
            {
                children.Add(parent.GetChild(i));
            }

            return children;
        }

        // Why exact type names: GetComponents(type) also returns derived components, whose count
        // would shift the index when a derived one sits before the recorded one.
        private static List<Component> SameTypeComponents(GameObject gameObject, string typeFullName)
        {
            List<Component> sameType = new List<Component>();
            foreach (Component candidate in gameObject.GetComponents<Component>())
            {
                if (candidate != null && string.Equals(candidate.GetType().FullName, typeFullName, StringComparison.Ordinal))
                {
                    sameType.Add(candidate);
                }
            }

            return sameType;
        }
    }
}
