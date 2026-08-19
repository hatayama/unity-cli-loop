#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness.Editor
{
    /// <summary>
    /// Builds the annotated-screenshot mismatch regression harness scene.
    /// </summary>
    public static class AnnotatedScreenshotMismatchSceneBuilder
    {
        private const string ScenePath = "Assets/RegressionHarness/AnnotatedScreenshotMismatch/AnnotatedScreenshotMismatch.unity";
        private const string UntaggedTag = "Untagged";
        private const string CameraName = "HarnessCamera";
        private const string EventSystemName = "EventSystem";
        private const string MainCanvasName = "Canvas_Main";
        private const string ButtonCenterBlockedName = "Button_CenterBlocked";
        private const string CenterBlockerName = "CenterBlocker";
        private const string PanelAName = "Panel_A";
        private const string DummyPrefix = "Dummy_";
        private const string ButtonDeepFrontName = "Button_DeepFront";
        private const string ButtonShallowBackName = "Button_ShallowBack";
        private const string NoRaycasterCanvasName = "Canvas_NoRaycaster";
        private const string ButtonNoRaycasterName = "Button_NoRaycaster";
        private const string WorldCanvasName = "Canvas_WorldSpace_NoCamera";
        private const string ButtonWorldNoCameraName = "Button_WorldNoCamera";
        private const int DummyChildCount = 5;
        private const float WorldCanvasWidth = 200f;
        private const float WorldCanvasHeight = 100f;
        private const float WorldCanvasScale = 0.01f;

        [MenuItem("UnityCliLoop/Build Annotated Screenshot Mismatch Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateHarnessCamera();
            CreateEventSystem();
            CreateMainCanvas();
            CreateNoRaycasterCanvas();
            CreateWorldSpaceCanvasWithoutCamera();

            bool saved = EditorSceneManager.SaveScene(scene, ScenePath);
            Debug.Assert(saved, $"[AnnotatedScreenshotMismatchSceneBuilder] Failed to save scene to {ScenePath}");
            if (!saved)
            {
                return;
            }

            Debug.Log($"[AnnotatedScreenshotMismatchSceneBuilder] Scene saved to {ScenePath}");
        }

        private static void CreateHarnessCamera()
        {
            GameObject cameraGo = new(CameraName);
            // why: S2 only reproduces when every camera fallback is null, including Camera.main
            cameraGo.tag = UntaggedTag;
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.12f, 0.12f, 0.16f, 1f);
            cameraGo.transform.position = new Vector3(0f, 1f, -10f);
            cameraGo.transform.LookAt(Vector3.zero);
        }

        private static void CreateEventSystem()
        {
            GameObject eventSystemGo = new(EventSystemName);
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();
        }

        private static void CreateMainCanvas()
        {
            GameObject canvasGo = CreateOverlayCanvas(MainCanvasName, addGraphicRaycaster: true);
            Transform canvasTransform = canvasGo.transform;

            CreateEmptyRect(canvasTransform, PanelAName, Vector2.zero, Vector2.zero);
            Transform panelA = canvasTransform.Find(PanelAName);
            for (int dummyIndex = 0; dummyIndex < DummyChildCount; dummyIndex++)
            {
                CreateEmptyRect(panelA, DummyPrefix + dummyIndex, Vector2.zero, Vector2.zero);
            }

            CreateUiButton(
                panelA,
                ButtonDeepFrontName,
                new Vector2(200f, 80f),
                new Vector2(300f, 200f),
                new Color(0.2f, 0.7f, 0.3f, 1f));

            CreateUiButton(
                canvasTransform,
                ButtonCenterBlockedName,
                new Vector2(300f, 120f),
                new Vector2(-300f, 200f),
                new Color(0.2f, 0.45f, 0.85f, 1f));

            // why not a child of Button_CenterBlocked: descendant hits count as reachable for the button
            CreateRaycastImage(
                canvasTransform,
                CenterBlockerName,
                new Vector2(80f, 80f),
                new Vector2(-300f, 200f),
                new Color(1f, 0f, 0f, 0.5f));

            CreateUiButton(
                canvasTransform,
                ButtonShallowBackName,
                new Vector2(200f, 80f),
                new Vector2(440f, 140f),
                new Color(0.9f, 0.75f, 0.2f, 1f));
        }

        private static void CreateNoRaycasterCanvas()
        {
            GameObject canvasGo = CreateOverlayCanvas(NoRaycasterCanvasName, addGraphicRaycaster: false);
            CreateUiButton(
                canvasGo.transform,
                ButtonNoRaycasterName,
                new Vector2(200f, 80f),
                new Vector2(-300f, -200f),
                new Color(0.55f, 0.55f, 0.55f, 1f));
        }

        private static void CreateWorldSpaceCanvasWithoutCamera()
        {
            GameObject canvasGo = new(WorldCanvasName);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = null;
            canvasGo.AddComponent<GraphicRaycaster>();

            RectTransform canvasRect = canvasGo.GetComponent<RectTransform>();
            canvasRect.position = new Vector3(0f, 1f, 0f);
            canvasRect.sizeDelta = new Vector2(WorldCanvasWidth, WorldCanvasHeight);
            canvasRect.localScale = Vector3.one * WorldCanvasScale;

            CreateUiButton(
                canvasGo.transform,
                ButtonWorldNoCameraName,
                new Vector2(WorldCanvasWidth, WorldCanvasHeight),
                Vector2.zero,
                new Color(0.2f, 0.75f, 0.8f, 1f));
        }

        private static GameObject CreateOverlayCanvas(string name, bool addGraphicRaycaster)
        {
            GameObject canvasGo = new(name);
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            if (addGraphicRaycaster)
            {
                canvasGo.AddComponent<GraphicRaycaster>();
            }

            return canvasGo;
        }

        private static void CreateUiButton(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 anchoredPosition,
            Color color)
        {
            GameObject buttonGo = CreateRaycastImage(parent, name, size, anchoredPosition, color);
            buttonGo.AddComponent<Button>();
        }

        private static GameObject CreateRaycastImage(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 anchoredPosition,
            Color color)
        {
            GameObject go = CreateRectObject(parent, name, size, anchoredPosition);
            Image image = go.AddComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return go;
        }

        private static void CreateEmptyRect(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 anchoredPosition)
        {
            CreateRectObject(parent, name, size, anchoredPosition);
        }

        private static GameObject CreateRectObject(
            Transform parent,
            string name,
            Vector2 size,
            Vector2 anchoredPosition)
        {
            GameObject go = new(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            return go;
        }
    }
}
