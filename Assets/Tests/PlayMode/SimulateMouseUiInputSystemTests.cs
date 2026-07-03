#if ULOOPMCP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    public class SimulateMouseUiInputSystemTests : InputTestFixture
    {
        private const float POSITION_TOLERANCE = 0.01f;

        private GameObject canvasGo = null!;
        private GameObject eventSystemGo = null!;
        private SimulateMouseUiTool tool = null!;
        private SimulateMouseUiResponse lastResponse = null!;

        public override void Setup()
        {
            base.Setup();

            canvasGo = new GameObject("TestCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            eventSystemGo = new GameObject("TestEventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();

            tool = new SimulateMouseUiTool();
            InputSystem.AddDevice<Mouse>();
        }

        public override void TearDown()
        {
            MouseDragState.Clear();
            Object.DestroyImmediate(canvasGo);
            Object.DestroyImmediate(eventSystemGo);
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator DragOneShot_Should_UpdateMouseCurrentPositionToDragPosition()
        {
            MouseAwareDragTracker tracker = CreateDraggableElement(
                "DragTarget", new Vector2(120f, 80f), new Vector2(200f, 100f));
            yield return null;

            Vector2 startScreenPosition = GetScreenPosition(tracker.gameObject);
            Vector2 endScreenPosition = startScreenPosition + new Vector2(140f, -60f);
            Vector2 startInputPosition = ScreenToInput(startScreenPosition);
            Vector2 endInputPosition = ScreenToInput(endScreenPosition);
            SetMousePosition(Vector2.zero);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Drag.ToString(),
                ["fromX"] = startInputPosition.x,
                ["fromY"] = startInputPosition.y,
                ["x"] = endInputPosition.x,
                ["y"] = endInputPosition.y,
                ["dragSpeed"] = 0f
            });

            Assert.IsTrue(lastResponse.Success);
            AssertPositionEquals(endScreenPosition, tracker.LastPointerPosition, "PointerEventData position should reach the drag end.");
            AssertPositionEquals(endScreenPosition, tracker.LastMousePosition, "Mouse.current position should match PointerEventData during drag.");
        }

        private IEnumerator RunTool(JObject parameters)
        {
            Task<BaseToolResponse> task = tool.ExecuteAsync(parameters);
            float timeoutAt = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() =>
                task.IsCompleted || Time.realtimeSinceStartup >= timeoutAt);
            Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
            Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");
            lastResponse = (SimulateMouseUiResponse)task.Result;
        }

        private MouseAwareDragTracker CreateDraggableElement(
            string name, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(canvasGo.transform, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            go.AddComponent<Image>();
            return go.AddComponent<MouseAwareDragTracker>();
        }

        private Vector2 GetScreenPosition(GameObject go)
        {
            return (Vector2)go.GetComponent<RectTransform>().position;
        }

        private Vector2 ScreenToInput(Vector2 screenPosition)
        {
            float targetHeight = Handles.GetMainGameViewSize().y;
            return new Vector2(screenPosition.x, targetHeight - screenPosition.y);
        }

        private void SetMousePosition(Vector2 position)
        {
            Mouse? currentMouse = Mouse.current;
            Assert.IsNotNull(currentMouse, "Mouse.current should exist after adding a Mouse device.");
            Set(currentMouse!.position, position);
        }

        private void AssertPositionEquals(Vector2 expected, Vector2 actual, string message)
        {
            float distance = Vector2.Distance(expected, actual);
            Assert.LessOrEqual(distance, POSITION_TOLERANCE, $"{message} Expected {expected}, got {actual}.");
        }
    }

    public class MouseAwareDragTracker : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public Vector2 LastPointerPosition { get; private set; }
        public Vector2 LastMousePosition { get; private set; }

        public void OnBeginDrag(PointerEventData eventData)
        {
        }

        public void OnDrag(PointerEventData eventData)
        {
            Mouse? currentMouse = Mouse.current;
            Assert.IsNotNull(currentMouse, "Mouse.current should exist for this test.");

            LastPointerPosition = eventData.position;
            LastMousePosition = currentMouse!.position.ReadValue();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
        }
    }
}
#endif
