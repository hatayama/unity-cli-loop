using UnityEngine;
using UnityEditor;

namespace io.github.hatayama.UnityCliLoop
{
    // Instantiates the InputVisualizationCanvas prefab and manages its lifecycle.
    internal static class OverlayCanvasFactory
    {
        private const string CANVAS_PREFAB_PATH = "Packages/io.github.hatayama.uloopmcp/Runtime/Common/InputVisualizationCanvas.prefab";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            _instance = null;
        }

        private static InputVisualizationCanvas _instance;

        public static InputVisualizationCanvas VisualizationCanvas
        {
            get
            {
                EnsureExists();
                Debug.Assert(_instance != null, "InputVisualizationCanvas instance must exist after EnsureExists");
                return _instance!;
            }
        }

        public static void EnsureExists()
        {
            if (_instance != null)
            {
                return;
            }

            // Domain Reload resets _instance but DontDestroyOnLoad objects survive; reclaim one and destroy duplicates
            InputVisualizationCanvas[] existing =
                Object.FindObjectsByType<InputVisualizationCanvas>(FindObjectsSortMode.None);
            for (int i = 0; i < existing.Length; i++)
            {
                if (_instance == null)
                {
                    _instance = existing[i];
                }
                else
                {
                    Object.DestroyImmediate(existing[i].gameObject);
                }
            }
            if (_instance != null)
            {
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CANVAS_PREFAB_PATH);
            Debug.Assert(prefab != null, $"InputVisualizationCanvas prefab not found at {CANVAS_PREFAB_PATH}");

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Object.DontDestroyOnLoad(go);
            _instance = go.GetComponent<InputVisualizationCanvas>();
            Debug.Assert(_instance != null, "InputVisualizationCanvas component not found on prefab");
        }
    }
}
