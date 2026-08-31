#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Test fixture that verifies mouse UI drags keep Mouse.current aligned with the simulated pointer position.
    /// </summary>
    public class SimulateMouseUiInputSystemTests : InputTestFixture
    {
        private const float PositionTolerance = 0.01f;

        private GameObject canvasGo = null!;
        private GameObject eventSystemGo = null!;
        private ExistingEventSystemDisableScope eventSystemDisableScope = null!;
        private SimulateMouseUiTool tool = null!;
        private SimulateMouseUiResponse lastResponse = null!;

        public override void Setup()
        {
            base.Setup();

            eventSystemDisableScope = new ExistingEventSystemDisableScope();

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
            eventSystemDisableScope.Restore();
            base.TearDown();
        }

        [UnityTest]
        public IEnumerator DragOneShot_Should_UpdateMouseCurrentPositionToDragPosition()
        {
            // Verifies a one-shot UI drag keeps Mouse.current aligned with PointerEventData at the drag end.
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

        [UnityTest]
        public IEnumerator DragSplit_Should_UpdateMouseCurrentPositionOnEachDragStep()
        {
            // Verifies the incremental drag (DragStart/DragMove/DragEnd) path keeps Mouse.current aligned too,
            // covering the InitiateDrag and InterpolateDragPosition sync points independently of one-shot drag.
            MouseAwareDragTracker tracker = CreateDraggableElement(
                "DragTarget", Vector2.zero, new Vector2(200f, 100f));
            yield return null;

            Vector2 startScreenPosition = GetScreenPosition(tracker.gameObject);
            Vector2 moveScreenPosition = startScreenPosition + new Vector2(50f, 0f);
            Vector2 endScreenPosition = startScreenPosition + new Vector2(100f, 0f);
            Vector2 startInputPosition = ScreenToInput(startScreenPosition);
            Vector2 moveInputPosition = ScreenToInput(moveScreenPosition);
            Vector2 endInputPosition = ScreenToInput(endScreenPosition);
            SetMousePosition(Vector2.zero);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragStart.ToString(),
                ["x"] = startInputPosition.x,
                ["y"] = startInputPosition.y
            });
            Assert.IsTrue(lastResponse.Success);
            AssertPositionEquals(startScreenPosition, GetMousePosition(), "Mouse.current position should match the drag start position.");

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragMove.ToString(),
                ["x"] = moveInputPosition.x,
                ["y"] = moveInputPosition.y,
                ["dragSpeed"] = 0f
            });
            Assert.IsTrue(lastResponse.Success);
            AssertPositionEquals(moveScreenPosition, GetMousePosition(), "Mouse.current position should match the drag move position.");

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragEnd.ToString(),
                ["x"] = endInputPosition.x,
                ["y"] = endInputPosition.y,
                ["dragSpeed"] = 0f
            });
            Assert.IsTrue(lastResponse.Success);
            AssertPositionEquals(endScreenPosition, GetMousePosition(), "Mouse.current position should match the drag end position.");
        }

        private IEnumerator RunTool(JObject parameters)
        {
            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(parameters, CancellationToken.None);
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

        private Vector2 GetMousePosition()
        {
            Mouse? currentMouse = Mouse.current;
            Assert.IsNotNull(currentMouse, "Mouse.current should exist after adding a Mouse device.");
            return currentMouse!.position.ReadValue();
        }

        private void AssertPositionEquals(Vector2 expected, Vector2 actual, string message)
        {
            float distance = Vector2.Distance(expected, actual);
            Assert.LessOrEqual(distance, PositionTolerance, $"{message} Expected {expected}, got {actual}.");
        }
    }

    /// <summary>
    /// Test support type that records both PointerEventData and Mouse.current positions observed during a drag.
    /// </summary>
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
