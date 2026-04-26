#nullable enable
using System;
using UnityEngine;

namespace io.github.hatayama.uLoopMCP
{
    // Builds slash-separated hierarchy paths shared by screenshot metadata and UI input targeting.
    internal static class GameObjectPathUtility
    {
        public static string GetFullPath(GameObject gameObject)
        {
            Debug.Assert(gameObject != null, "GameObject must not be null.");
            if (gameObject == null)
            {
                throw new ArgumentNullException(nameof(gameObject));
            }

            if (gameObject.transform.parent == null)
            {
                return gameObject.name;
            }

            return GetFullPath(gameObject.transform.parent.gameObject) + "/" + gameObject.name;
        }
    }
}
