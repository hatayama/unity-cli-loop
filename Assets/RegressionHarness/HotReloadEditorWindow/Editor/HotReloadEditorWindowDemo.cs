using UnityEditor;
using UnityEngine;

namespace io.github.hatayama.UnityCliLoop.RegressionHarness
{
    // Minimal EditorWindow demo for manual hot-reload checks in EditMode: a square orbits the
    // window center while the Editor is not playing. The tunable values are literals inside
    // method bodies on purpose: radius, squareSize, and squareColor in DrawOrbit, and
    // degreesPerSecond in Advance. Edit one and run `uloop hot-reload --files <this file>`
    // to see it change without PlayMode and without a domain reload.
    // Why body literals (not a const or field): hot-reload shims contain only the rewritten
    // method body, so anything outside the body is read from the stale compiled assembly and a
    // hot-reload of it would be a silent no-op.
    public sealed class HotReloadEditorWindowDemo : EditorWindow
    {
        private const string WindowTitle = "HotReload EditMode Demo";
        private double _lastUpdateTime;
        private float _angleDegrees;

        [MenuItem("Window/uloop/HotReload EditMode Demo")]
        private static void Open()
        {
            HotReloadEditorWindowDemo window = GetWindow<HotReloadEditorWindowDemo>(WindowTitle);
            window.minSize = new Vector2(400f, 400f);
            window.Show();
        }

        private void OnEnable()
        {
            _lastUpdateTime = EditorApplication.timeSinceStartup;
            EditorApplication.update += Tick;
        }

        private void OnDisable()
        {
            EditorApplication.update -= Tick;
        }

        // Why a separate update hook: OnGUI only runs on repaint, so without this the square
        // would freeze until the mouse moves over the window.
        private void Tick()
        {
            double now = EditorApplication.timeSinceStartup;
            float deltaTime = (float)(now - _lastUpdateTime);
            _lastUpdateTime = now;
            Advance(deltaTime);
            Repaint();
        }

        private void Advance(float deltaTime)
        {
            float degreesPerSecond = 90f;
            // Why wrap: the window can stay open for hours, and an unbounded float loses precision.
            _angleDegrees = (_angleDegrees + degreesPerSecond * deltaTime) % 360f;
        }

        private void OnGUI()
        {
            EditorGUI.DrawRect(new Rect(0f, 0f, position.width, position.height), new Color(0.1f, 0.1f, 0.12f, 1f));
            DrawOrbit();
        }

        private void DrawOrbit()
        {
            float radius = 120f;
            float squareSize = 60f;
            Color squareColor = new Color(0.2f, 0.7f, 1f, 1f);

            Vector2 center = new Vector2(position.width * 0.5f, position.height * 0.5f);
            float radians = _angleDegrees * Mathf.Deg2Rad;
            Vector2 squareCenter = center + new Vector2(Mathf.Cos(radians), -Mathf.Sin(radians)) * radius;

            Matrix4x4 previousMatrix = GUI.matrix;
            GUIUtility.RotateAroundPivot(-_angleDegrees, squareCenter);
            EditorGUI.DrawRect(
                new Rect(squareCenter.x - squareSize * 0.5f, squareCenter.y - squareSize * 0.5f, squareSize, squareSize),
                squareColor);
            GUI.matrix = previousMatrix;

            GUI.Label(new Rect(8f, 8f, position.width - 16f, 20f), "EditMode hot-reload demo: edit DrawOrbit / Advance literals");
        }
    }
}
