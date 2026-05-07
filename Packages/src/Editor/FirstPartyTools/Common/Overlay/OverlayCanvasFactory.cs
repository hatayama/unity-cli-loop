using UnityEngine;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    // Instantiates the InputVisualizationCanvas prefab and manages its lifecycle.
    /// <summary>
    /// Provides Overlay Canvas Factory operations for its owning module.
    /// </summary>
    internal sealed class OverlayCanvasFactoryService
    {
        private const string CANVAS_PREFAB_PATH = "Packages/io.github.hatayama.uloopmcp/Runtime/Common/InputVisualizationCanvas.prefab";

        private InputVisualizationCanvas _instance;

        public InputVisualizationCanvas VisualizationCanvas
        {
            get
            {
                EnsureExists();
                Debug.Assert(_instance != null, "InputVisualizationCanvas instance must exist after EnsureExists");
                return _instance!;
            }
        }

        public void Reset()
        {
            _instance = null;
        }

        public void EnsureExists()
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

    /// <summary>
    /// Creates Overlay Canvas instances with the dependencies required by this module.
    /// </summary>
    internal static class OverlayCanvasFactory
    {
        private static readonly OverlayCanvasFactoryService ServiceValue = new OverlayCanvasFactoryService();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStaticFields()
        {
            ServiceValue.Reset();
        }

        public static InputVisualizationCanvas VisualizationCanvas => ServiceValue.VisualizationCanvas;

        public static void EnsureExists()
        {
            ServiceValue.EnsureExists();
        }
    }
}
