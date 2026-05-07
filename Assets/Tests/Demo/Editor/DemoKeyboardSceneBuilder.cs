#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

using io.github.hatayama.UnityCliLoop.Tests.Demo;

namespace io.github.hatayama.UnityCliLoop.Tests.Demo.Editor
{
    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public static class DemoKeyboardSceneBuilder
    {
        private const float KEY_SIZE = 100f;
        private const float FKEY_HEIGHT = 70f;
        private const float KEY_GAP = 6f;
        private const float ROW_GAP = 6f;
        private const string SCENE_PATH = "Assets/Scenes/SimulateKeyboardDemoScene.unity";
        private const string CUBE_NAME = "KeyboardInputCube";

        private static bool IsMac =>
            UnityEngine.Application.platform == RuntimePlatform.OSXEditor ||
            UnityEngine.Application.platform == RuntimePlatform.OSXPlayer;

        private struct KeyDef
        {
            public Key Key;
            public string Label;
            public float Width;
            public float Height;

            public KeyDef(Key key, string label, float width = KEY_SIZE, float height = KEY_SIZE)
            {
                Key = key;
                Label = label;
                Width = width;
                Height = height;
            }
        }

        [MenuItem("UnityCliLoop/Build Keyboard Demo Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            GameObject cameraGo = new("Main Camera");
            Camera camera = cameraGo.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.08f, 0.09f, 0.12f, 1f);
            camera.transform.position = new Vector3(0f, 4f, -7f);
            camera.transform.rotation = Quaternion.Euler(30f, 0f, 0f);

            CreateDirectionalLight();
            CreateGround();
            CreateKeyboardInputCube();

            GameObject eventSystemGo = new("EventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<InputSystemUIInputModule>();

            GameObject canvasGo = new("Canvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            CanvasScaler scaler = canvasGo.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            canvasGo.AddComponent<GraphicRaycaster>();

            CreateTitle(canvasGo.transform);
            CreateKeyboard(canvasGo.transform);
            CreateStatusText(canvasGo.transform);

            bool saved = EditorSceneManager.SaveScene(scene, SCENE_PATH);
            if (!saved)
            {
                Debug.Assert(false, $"[DemoKeyboardSceneBuilder] Failed to save scene to {SCENE_PATH}");
                return;
            }
            Debug.Log($"[DemoKeyboardSceneBuilder] Scene saved to {SCENE_PATH}");
        }

        private static void CreateDirectionalLight()
        {
            GameObject lightGo = new("Directional Light");
            Light light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.96f, 0.84f);
            light.intensity = 1.2f;
            lightGo.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        }

        private static void CreateGround()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "KeyboardInputGround";
            ground.transform.position = Vector3.zero;
            ground.transform.localScale = new Vector3(5f, 1f, 5f);

            Renderer renderer = ground.GetComponent<Renderer>();
            renderer.material.color = new Color(0.16f, 0.20f, 0.24f);
        }

        private static void CreateKeyboardInputCube()
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = CUBE_NAME;
            cube.transform.position = new Vector3(0f, 0.5f, 0f);
            cube.transform.localScale = Vector3.one;

            Renderer renderer = cube.GetComponent<Renderer>();
            renderer.material.color = Color.white;
            cube.AddComponent<DemoKeyboardMover>();
        }

        private static void CreateTitle(Transform parent)
        {
            GameObject go = new("Title");
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = new Vector2(0f, -20f);
            rect.sizeDelta = new Vector2(600f, 50f);

            Text text = go.AddComponent<Text>();
            text.text = "Keyboard Input Demo";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 32;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;
        }

        private static void CreateStatusText(Transform parent)
        {
            GameObject go = new("StatusText");
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0f);
            rect.anchorMax = new Vector2(0.5f, 0f);
            rect.pivot = new Vector2(0.5f, 0f);
            rect.anchoredPosition = new Vector2(0f, 20f);
            rect.sizeDelta = new Vector2(800f, 40f);

            Text text = go.AddComponent<Text>();
            text.text = "Press keys using simulate-keyboard tool";
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = 20;
            text.color = new Color(0.7f, 0.7f, 0.7f, 1f);
            text.alignment = TextAnchor.MiddleCenter;
        }

        private static void CreateKeyboard(Transform parent)
        {
            GameObject panel = new("KeyboardPanel");
            panel.transform.SetParent(parent, false);

            RectTransform panelRect = panel.AddComponent<RectTransform>();
            panelRect.anchorMin = new Vector2(0.5f, 0.5f);
            panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
            panelRect.anchoredPosition = Vector2.zero;
            panelRect.sizeDelta = new Vector2(1800f, 900f);

            float startY = 300f;

            // Row 0: Esc + F1-F12
            KeyDef[] row0 = {
                new KeyDef(Key.Escape, "Esc", 110f, FKEY_HEIGHT),
                new KeyDef(Key.F1, "F1", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F2, "F2", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F3, "F3", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F4, "F4", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F5, "F5", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F6, "F6", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F7, "F7", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F8, "F8", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F9, "F9", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F10, "F10", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F11, "F11", KEY_SIZE, FKEY_HEIGHT),
                new KeyDef(Key.F12, "F12", KEY_SIZE, FKEY_HEIGHT),
            };
            BuildRow(panel.transform, row0, startY);

            // Row 1: 1-0
            float row1Y = startY - (FKEY_HEIGHT + ROW_GAP + 8f);
            KeyDef[] row1 = {
                new KeyDef(Key.Digit1, "1"), new KeyDef(Key.Digit2, "2"),
                new KeyDef(Key.Digit3, "3"), new KeyDef(Key.Digit4, "4"),
                new KeyDef(Key.Digit5, "5"), new KeyDef(Key.Digit6, "6"),
                new KeyDef(Key.Digit7, "7"), new KeyDef(Key.Digit8, "8"),
                new KeyDef(Key.Digit9, "9"), new KeyDef(Key.Digit0, "0"),
            };
            BuildRow(panel.transform, row1, row1Y);

            // Row 2: Tab Q W E R T Y U I O P
            float row2Y = row1Y - (KEY_SIZE + ROW_GAP);
            KeyDef[] row2 = {
                new KeyDef(Key.Tab, "Tab", 140f),
                new KeyDef(Key.Q, "Q"), new KeyDef(Key.W, "W"),
                new KeyDef(Key.E, "E"), new KeyDef(Key.R, "R"),
                new KeyDef(Key.T, "T"), new KeyDef(Key.Y, "Y"),
                new KeyDef(Key.U, "U"), new KeyDef(Key.I, "I"),
                new KeyDef(Key.O, "O"), new KeyDef(Key.P, "P"),
            };
            BuildRow(panel.transform, row2, row2Y);

            // Row 3: A S D F G H J K L Enter
            float row3Y = row2Y - (KEY_SIZE + ROW_GAP);
            KeyDef[] row3 = {
                new KeyDef(Key.A, "A"), new KeyDef(Key.S, "S"),
                new KeyDef(Key.D, "D"), new KeyDef(Key.F, "F"),
                new KeyDef(Key.G, "G"), new KeyDef(Key.H, "H"),
                new KeyDef(Key.J, "J"), new KeyDef(Key.K, "K"),
                new KeyDef(Key.L, "L"),
                new KeyDef(Key.Enter, "Enter", 180f),
            };
            BuildRow(panel.transform, row3, row3Y);

            // Row 4: LShift Z X C V B N M
            float row4Y = row3Y - (KEY_SIZE + ROW_GAP);
            KeyDef[] row4 = {
                new KeyDef(Key.LeftShift, "Shift", 170f),
                new KeyDef(Key.Z, "Z"), new KeyDef(Key.X, "X"),
                new KeyDef(Key.C, "C"), new KeyDef(Key.V, "V"),
                new KeyDef(Key.B, "B"), new KeyDef(Key.N, "N"),
                new KeyDef(Key.M, "M"),
            };
            BuildRow(panel.transform, row4, row4Y);

            // Row 5: LCtrl LAlt LMeta Space RMeta RAlt RCtrl
            float row5Y = row4Y - (KEY_SIZE + ROW_GAP);
            string metaLabel = IsMac ? "\u2318" : "Win";
            string altLabel = IsMac ? "\u2325" : "Alt";
            string ctrlLabel = IsMac ? "\u2303" : "Ctrl";
            KeyDef[] row5 = {
                new KeyDef(Key.LeftCtrl, ctrlLabel, 120f),
                new KeyDef(Key.LeftAlt, altLabel, 120f),
                new KeyDef(Key.LeftMeta, metaLabel, 120f),
                new KeyDef(Key.Space, "Space", 380f),
                new KeyDef(Key.RightMeta, metaLabel, 120f),
                new KeyDef(Key.RightAlt, altLabel, 120f),
                new KeyDef(Key.RightCtrl, ctrlLabel, 120f),
            };
            BuildRow(panel.transform, row5, row5Y);

            // Arrow keys (right side, cross pattern)
            float arrowBaseX = 580f;
            float arrowBaseY = row5Y;
            CreateKey(panel.transform, new KeyDef(Key.UpArrow, "\u2191"), arrowBaseX, arrowBaseY + KEY_SIZE + ROW_GAP);
            CreateKey(panel.transform, new KeyDef(Key.LeftArrow, "\u2190"), arrowBaseX - KEY_SIZE - KEY_GAP, arrowBaseY);
            CreateKey(panel.transform, new KeyDef(Key.DownArrow, "\u2193"), arrowBaseX, arrowBaseY);
            CreateKey(panel.transform, new KeyDef(Key.RightArrow, "\u2192"), arrowBaseX + KEY_SIZE + KEY_GAP, arrowBaseY);
        }

        private static void BuildRow(Transform parent, KeyDef[] keys, float y)
        {
            float totalWidth = 0f;
            for (int i = 0; i < keys.Length; i++)
            {
                totalWidth += keys[i].Width;
                if (i < keys.Length - 1)
                {
                    totalWidth += KEY_GAP;
                }
            }

            float x = -totalWidth / 2f;
            for (int i = 0; i < keys.Length; i++)
            {
                float keyX = x + keys[i].Width / 2f;
                CreateKey(parent, keys[i], keyX, y);
                x += keys[i].Width + KEY_GAP;
            }
        }

        private static void CreateKey(Transform parent, KeyDef keyDef, float x, float y)
        {
            GameObject go = new($"Key_{keyDef.Key}");
            go.transform.SetParent(parent, false);

            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = new Vector2(x, y);
            rect.sizeDelta = new Vector2(keyDef.Width, keyDef.Height);

            Image bg = go.AddComponent<Image>();
            bg.color = new Color(0.25f, 0.25f, 0.25f, 1f);

            DemoKeyIndicator indicator = go.AddComponent<DemoKeyIndicator>();

            // Label
            GameObject labelGo = new("Label");
            labelGo.transform.SetParent(go.transform, false);

            RectTransform labelRect = labelGo.AddComponent<RectTransform>();
            labelRect.anchorMin = Vector2.zero;
            labelRect.anchorMax = Vector2.one;
            labelRect.offsetMin = Vector2.zero;
            labelRect.offsetMax = Vector2.zero;

            Text text = labelGo.AddComponent<Text>();
            text.text = keyDef.Label;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            text.fontSize = keyDef.Height < KEY_SIZE ? 20 : 26;
            text.fontStyle = FontStyle.Bold;
            text.color = Color.white;
            text.alignment = TextAnchor.MiddleCenter;

            indicator.Init(keyDef.Key, keyDef.Label);
        }
    }
}
#endif
