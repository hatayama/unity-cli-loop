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

        // Why no create: screenshot must hide an already-visible overlay without spawning one.
        public GameObject TryGetExisting()
        {
            ReclaimExistingInstance();
            if (_instance == null)
            {
                return null;
            }

            return _instance.gameObject;
        }

        public void EnsureExists()
        {
            if (_instance != null)
            {
                // Why reactivate: screenshot hide leaves the canvas inactive; the next simulate-*
                // call must bring it back without instantiating a second DontDestroyOnLoad copy.
                EnsureActive(_instance.gameObject);
                return;
            }

            ReclaimExistingInstance();
            if (_instance != null)
            {
                EnsureActive(_instance.gameObject);
                return;
            }

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(CANVAS_PREFAB_PATH);
            Debug.Assert(prefab != null, $"InputVisualizationCanvas prefab not found at {CANVAS_PREFAB_PATH}");

            GameObject go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            Object.DontDestroyOnLoad(go);
            _instance = go.GetComponent<InputVisualizationCanvas>();
            Debug.Assert(_instance != null, "InputVisualizationCanvas component not found on prefab");
        }

        // Domain Reload resets _instance but DontDestroyOnLoad objects survive; reclaim one and destroy duplicates.
        private void ReclaimExistingInstance()
        {
            if (_instance != null)
            {
                return;
            }

            // Why include inactive: screenshot SetActive(false) would otherwise hide the only canvas
            // from default FindObjectsByType and make EnsureExists spawn a duplicate.
#if UNITY_6000_4_OR_NEWER
            InputVisualizationCanvas[] existing = Object.FindObjectsByType<InputVisualizationCanvas>(
                FindObjectsInactive.Include);
#else
            InputVisualizationCanvas[] existing = Object.FindObjectsByType<InputVisualizationCanvas>(
                FindObjectsInactive.Include,
                FindObjectsSortMode.None);
#endif
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
        }

        private static void EnsureActive(GameObject overlayRoot)
        {
            // Why Canvas too: screenshot hide disables Canvas.enabled as well as the GameObject.
            // Recovering only activeSelf leaves badges permanently invisible while looking active.
            Canvas overlayCanvas = overlayRoot.GetComponent<Canvas>();
            if (overlayCanvas != null && !overlayCanvas.enabled)
            {
                overlayCanvas.enabled = true;
            }

            if (!overlayRoot.activeSelf)
            {
                overlayRoot.SetActive(true);
            }
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

        public static GameObject TryGetExisting()
        {
            return ServiceValue.TryGetExisting();
        }
    }
}
