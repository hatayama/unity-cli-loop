#if ULOOPMCP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace io.github.hatayama.uLoopMCP
{
    public static class InputReplayVerificationSceneBuilder
    {
        private const string SCENE_PATH = "Assets/Scenes/InputReplayVerificationScene.unity";

        [MenuItem("uLoopMCP/Build InputReplay Verification Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            CreateCamera();
            CreateEventSystem();
            GameObject cube = CreateCube();
            CreateUI(cube);

            bool saved = EditorSceneManager.SaveScene(scene, SCENE_PATH);
            if (!saved)
            {
                Debug.Assert(false, $"[InputReplayVerificationSceneBuilder] Failed to save scene to {SCENE_PATH}");
                return;
            }
            Debug.Log($"[InputReplayVerificationSceneBuilder] Scene saved to {SCENE_PATH}");
        }

        private static void CreateCamera()
        {
            GameObject cameraGo = new GameObject("Main Camera");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.15f, 0.15f, 0.2f, 1f);
            cameraGo.transform.position = new Vector3(0f, 5f, -8f);
            cameraGo.transform.eulerAngles = new Vector3(30f, 0f, 0f);
        }

        private static void CreateEventSystem()
        {
            GameObject eventSystemGo = new GameObject("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();
        }

        private static GameObject CreateCube()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "VerificationCube";
            cube.transform.position = Vector3.zero;
            cube.transform.localScale = Vector3.one;

            // Physics would make the scene non-deterministic
            Collider? collider = cube.GetComponent<Collider>();
            if (collider != null)
            {
                Object.DestroyImmediate(collider);
            }

            Rigidbody? rb = cube.GetComponent<Rigidbody>();
            if (rb != null)
            {
                Object.DestroyImmediate(rb);
            }

            return cube;
        }

        private static void CreateUI(GameObject cube)
        {
            GameObject canvasGo = new GameObject("Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            // Title
            CreateText(canvasGo.transform, "Title",
                "Input Replay Verification Scene",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -20f), new Vector2(600f, 40f),
                28, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);

            // Info panel (top-left)
            float yOffset = -20f;
            float lineHeight = 30f;

            Text frameText = CreateText(canvasGo.transform, "FrameText",
                "Frame: 0",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, yOffset), new Vector2(400f, lineHeight),
                20, FontStyle.Normal, Color.green, TextAnchor.MiddleLeft);

            Text positionText = CreateText(canvasGo.transform, "PositionText",
                "Pos: (0, 0, 0)",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, yOffset - lineHeight), new Vector2(400f, lineHeight),
                20, FontStyle.Normal, Color.green, TextAnchor.MiddleLeft);

            Text rotationText = CreateText(canvasGo.transform, "RotationText",
                "Rot Y: 0.00",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, yOffset - lineHeight * 2), new Vector2(400f, lineHeight),
                20, FontStyle.Normal, Color.green, TextAnchor.MiddleLeft);

            Text scaleText = CreateText(canvasGo.transform, "ScaleText",
                "Scale: 1.00",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, yOffset - lineHeight * 3), new Vector2(400f, lineHeight),
                20, FontStyle.Normal, Color.green, TextAnchor.MiddleLeft);

            Text inputText = CreateText(canvasGo.transform, "InputText",
                "Input: [none]",
                new Vector2(0f, 1f), new Vector2(0f, 1f), new Vector2(0f, 1f),
                new Vector2(20f, yOffset - lineHeight * 4), new Vector2(400f, lineHeight),
                20, FontStyle.Normal, Color.yellow, TextAnchor.MiddleLeft);

            GameObject startPanel = CreateStartPanel(canvasGo.transform, cube);

            // Stop panel (shown during recording/replay, initially hidden)
            GameObject stopPanel = CreateStopPanel(canvasGo.transform);
            stopPanel.SetActive(false);

            CreateText(canvasGo.transform, "Instructions",
                "WASD: Move | Mouse: Rotate | LClick: Red toggle | RClick: Blue toggle | Scroll: Scale",
                new Vector2(0.5f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 20f), new Vector2(900f, 30f),
                16, FontStyle.Normal, new Color(0.7f, 0.7f, 0.7f, 1f), TextAnchor.MiddleCenter);

            // Verify panel (bottom-right, initially hidden)
            GameObject verifyPanel = CreateVerifyPanel(canvasGo.transform);
            verifyPanel.SetActive(false);

            MeshRenderer? renderer = cube.GetComponent<MeshRenderer>();
            InputReplayVerificationController controller = cube.AddComponent<InputReplayVerificationController>();
            SerializedObject so = new SerializedObject(controller);
            so.FindProperty("_frameText").objectReferenceValue = frameText;
            so.FindProperty("_positionText").objectReferenceValue = positionText;
            so.FindProperty("_rotationText").objectReferenceValue = rotationText;
            so.FindProperty("_scaleText").objectReferenceValue = scaleText;
            so.FindProperty("_inputText").objectReferenceValue = inputText;
            so.FindProperty("_startPanel").objectReferenceValue = startPanel;
            so.FindProperty("_stopPanel").objectReferenceValue = stopPanel;
            so.FindProperty("_verifyPanel").objectReferenceValue = verifyPanel;
            so.FindProperty("_verifyResultText").objectReferenceValue = verifyPanel.transform.Find("ResultText").GetComponent<Text>();
            so.FindProperty("_cubeRenderer").objectReferenceValue = renderer;
            so.ApplyModifiedPropertiesWithoutUndo();

            Button recordButton = startPanel.transform.Find("RecordButton").GetComponent<Button>();
            Button replayButton = startPanel.transform.Find("ReplayButton").GetComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(recordButton.onClick, controller.OnStartRecording);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(replayButton.onClick, controller.OnStartReplay);

            Button stopButton = stopPanel.transform.Find("StopButton").GetComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(stopButton.onClick, controller.OnStop);

            Button saveRecBtn = verifyPanel.transform.Find("SaveRecordingLogButton").GetComponent<Button>();
            Button saveRepBtn = verifyPanel.transform.Find("SaveReplayLogButton").GetComponent<Button>();
            Button compareBtn = verifyPanel.transform.Find("CompareLogsButton").GetComponent<Button>();
            UnityEditor.Events.UnityEventTools.AddPersistentListener(saveRecBtn.onClick, controller.OnSaveRecordingLog);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(saveRepBtn.onClick, controller.OnSaveReplayLog);
            UnityEditor.Events.UnityEventTools.AddPersistentListener(compareBtn.onClick, controller.OnCompareLogs);
        }

        private static GameObject CreateStartPanel(Transform canvasTransform, GameObject cube)
        {
            GameObject panel = new GameObject("StartPanel");
            panel.transform.SetParent(canvasTransform, false);

            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, -60f);
            panelRect.sizeDelta = new Vector2(500f, 120f);

            CreateText(panel.transform, "Title",
                "Select Mode",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, 0f), new Vector2(400f, 40f),
                32, FontStyle.Bold, new Color(1f, 0.9f, 0.3f, 1f), TextAnchor.MiddleCenter);

            CreateButton(panel.transform, "RecordButton", "\u25cf Start Recording",
                new Vector2(0.25f, 0f), -80f, new Color(0.8f, 0.2f, 0.2f, 1f));

            CreateButton(panel.transform, "ReplayButton", "\u25b6 Start Replay",
                new Vector2(0.75f, 0f), -80f, new Color(0.2f, 0.6f, 0.8f, 1f));

            return panel;
        }

        private static GameObject CreateStopPanel(Transform canvasTransform)
        {
            GameObject panel = new GameObject("StopPanel");
            panel.transform.SetParent(canvasTransform, false);

            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = new Vector2(0f, -60f);
            panelRect.sizeDelta = new Vector2(300f, 80f);

            CreateButton(panel.transform, "StopButton", "\u25a0 Stop",
                new Vector2(0.5f, 0.5f), 0f, new Color(0.7f, 0.2f, 0.2f, 1f));

            return panel;
        }

        private static GameObject CreateVerifyPanel(Transform canvasTransform)
        {
            GameObject panel = new GameObject("VerifyPanel");
            panel.transform.SetParent(canvasTransform, false);

            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(1f, 0f);
            panelRect.anchorMax = new Vector2(1f, 0f);
            panelRect.pivot = new Vector2(1f, 0f);
            panelRect.anchoredPosition = new Vector2(-20f, 60f);
            panelRect.sizeDelta = new Vector2(640f, 400f);

            Image panelBg = panel.AddComponent<Image>();
            panelBg.color = new Color(0f, 0f, 0f, 0.5f);

            CreateText(panel.transform, "VerifyTitle",
                "Verification",
                new Vector2(0.5f, 1f), new Vector2(0.5f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -10f), new Vector2(600f, 40f),
                28, FontStyle.Bold, Color.white, TextAnchor.MiddleCenter);

            CreateButton(panel.transform, "SaveRecordingLogButton", "Save Recording Log",
                new Vector2(0.5f, 1f), -60f, new Color(0.3f, 0.5f, 0.3f, 1f));

            CreateButton(panel.transform, "SaveReplayLogButton", "Save Replay Log",
                new Vector2(0.5f, 1f), -110f, new Color(0.3f, 0.4f, 0.6f, 1f));

            CreateButton(panel.transform, "CompareLogsButton", "\u2714 Compare Logs",
                new Vector2(0.5f, 1f), -160f, new Color(0.5f, 0.4f, 0.2f, 1f));

            Text resultText = CreateText(panel.transform, "ResultText",
                "",
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 10f), new Vector2(-20f, 180f),
                18, FontStyle.Normal, Color.white, TextAnchor.UpperLeft);

            return panel;
        }

        private static void CreateButton(Transform parent, string name, string label, Vector2 anchorPos, float yOffset, Color bgColor)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(anchorPos.x, anchorPos.y);
            rect.anchorMax = new Vector2(anchorPos.x, anchorPos.y);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(0f, yOffset);
            rect.sizeDelta = new Vector2(220f, 50f);

            Image image = go.AddComponent<Image>();
            image.color = bgColor;

            go.AddComponent<Button>();

            GameObject textGo = new GameObject("Text");
            textGo.transform.SetParent(go.transform, false);

            RectTransform textRect = textGo.AddComponent<RectTransform>();
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = Vector2.zero;
            textRect.offsetMax = Vector2.zero;

            Text text = textGo.AddComponent<Text>();
            text.text = label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 22;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
        }

        private static Text CreateText(
            Transform parent, string name, string content,
            Vector2 anchorMin, Vector2 anchorMax, Vector2 pivot,
            Vector2 anchoredPosition, Vector2 sizeDelta,
            int fontSize, FontStyle fontStyle, Color color, TextAnchor alignment)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;

            Text text = go.AddComponent<Text>();
            text.text = content;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = fontSize;
            text.fontStyle = fontStyle;
            text.color = color;
            text.alignment = alignment;

            return text;
        }
    }
}
#endif
