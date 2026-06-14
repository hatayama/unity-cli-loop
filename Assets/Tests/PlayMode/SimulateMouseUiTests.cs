#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using io.github.hatayama.uLoopMCP;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    public class SimulateMouseUiTests
    {
        private GameObject canvasGo = null!;
        private GameObject eventSystemGo = null!;
        private SimulateMouseUiTool tool = null!;
        private SimulateMouseUiResponse lastResponse = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            canvasGo = new GameObject("TestCanvas");
            Canvas canvas = canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.AddComponent<CanvasScaler>();
            canvasGo.AddComponent<GraphicRaycaster>();

            eventSystemGo = new GameObject("TestEventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            eventSystemGo.AddComponent<StandaloneInputModule>();

            tool = new SimulateMouseUiTool();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            MouseDragState.Clear();
            Object.Destroy(canvasGo);
            Object.Destroy(eventSystemGo);
            yield return null;
        }

        #region Click Tests

        [UnityTest]
        public IEnumerator Click_Should_FirePointerEvents()
        {
            ClickTracker tracker = CreateClickableElement("ClickTarget", Vector2.zero, new Vector2(200, 100));
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Click.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.PointerDownCalled, "PointerDown should be fired");
            Assert.IsTrue(tracker.PointerUpCalled, "PointerUp should be fired");
            Assert.IsTrue(tracker.PointerClickCalled, "PointerClick should be fired");
            Assert.AreEqual("ClickTarget", lastResponse.HitGameObjectName);
        }

        [UnityTest]
        public IEnumerator Click_AtEmptyPosition_Should_SucceedWithNoHit()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Click.ToString(),
                ["x"] = 1,
                ["y"] = 1
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsNull(lastResponse.HitGameObjectName);
        }

        [UnityTest]
        public IEnumerator Click_WithBypassRaycast_Should_ClickTargetBehindBlocker()
        {
            ClickTracker tracker = CreateClickableElement("ClickTarget", Vector2.zero, new Vector2(200, 100));
            GameObject blocker = CreateUIElement("Blocker", Vector2.zero, new Vector2(240, 140));
            blocker.AddComponent<Image>();
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Click.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y,
                ["bypassRaycast"] = true,
                ["targetPath"] = "TestCanvas/ClickTarget"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.PointerDownCalled, "PointerDown should be fired");
            Assert.IsTrue(tracker.PointerUpCalled, "PointerUp should be fired");
            Assert.IsTrue(tracker.PointerClickCalled, "PointerClick should be fired");
            Assert.AreEqual("ClickTarget", lastResponse.HitGameObjectName);
        }

        // Verifies that an overlay UI element clipped out of EventSystem's Screen bounds
        // (scaled Game view resolution) still wins over a non-GraphicRaycaster hit,
        // because ScreenSpaceOverlay UI always renders in front of world-space content.
        [UnityTest]
        public IEnumerator Click_Should_PreferClippedOverlayUiOverNonUiRaycastHit()
        {
            GameObject nonUiRoot = new GameObject("NonUiRaycasterRoot");

            try
            {
                GameObject nonUiTarget = new GameObject("NonUiTarget");
                nonUiTarget.transform.SetParent(nonUiRoot.transform, false);
                ClickTracker nonUiTracker = nonUiTarget.AddComponent<ClickTracker>();
                AlwaysHitRaycaster nonUiRaycaster = nonUiRoot.AddComponent<AlwaysHitRaycaster>();
                nonUiRaycaster.Target = nonUiTarget;

                // Place the UI element beyond Screen.width so GraphicRaycaster clips it
                // while the Canvas-space hit test still reaches it.
                Vector2 offscreenOffset = new Vector2(Screen.width, 0f);
                ClickTracker overlayTracker = CreateClickableElement(
                    "OffscreenOverlayTarget", offscreenOffset, new Vector2(200f, 100f));
                yield return null;

                Vector2 screenPos = GetScreenPosition(overlayTracker.gameObject);
                PointerEventData pointerData = new PointerEventData(EventSystem.current)
                {
                    position = screenPos
                };
                List<RaycastResult> raycastResults = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, raycastResults);

                Assert.IsNotEmpty(raycastResults, "Setup: the non-UI raycaster should hit.");
                Assert.IsFalse(raycastResults[0].module is GraphicRaycaster,
                    "Setup: EventSystem's first hit should not come from a GraphicRaycaster.");
                Assert.IsFalse(
                    raycastResults.Exists(result => result.gameObject == overlayTracker.gameObject),
                    "Setup: overlay UI must be clipped out of EventSystem results for this regression test.");

                yield return RunTool(new JObject
                {
                    ["action"] = MouseAction.Click.ToString(),
                    ["x"] = screenPos.x,
                    ["y"] = screenPos.y
                });

                Assert.IsTrue(lastResponse.Success);
                Assert.IsTrue(overlayTracker.PointerClickCalled, "Overlay UI should receive the click");
                Assert.IsFalse(nonUiTracker.PointerClickCalled, "Non-UI target behind overlay UI should not receive the click");
                Assert.AreEqual("OffscreenOverlayTarget", lastResponse.HitGameObjectName);
            }
            finally
            {
                Object.Destroy(nonUiRoot);
            }
        }

        // Verifies the real Physics2D raycaster path. Physics2DRaycaster cannot hit
        // outside its camera pixelRect, so the test raises its priority instead of
        // using the Screen-bounds clipping setup from the synthetic raycaster tests.
        [UnityTest]
        public IEnumerator Click_Should_PreferOverlayUiOverPhysics2DRaycastHit()
        {
            GameObject physicsRoot = new GameObject("Physics2DRaycasterRoot");

            try
            {
                GameObject cameraGo = new GameObject("Physics2DCamera");
                cameraGo.transform.SetParent(physicsRoot.transform, false);
                cameraGo.transform.position = new Vector3(0f, 0f, -10f);
                Camera physicsCamera = cameraGo.AddComponent<Camera>();
                physicsCamera.orthographic = true;
                physicsCamera.orthographicSize = 5f;
                cameraGo.AddComponent<HighPriorityPhysics2DRaycaster>();

                canvasGo.GetComponent<Canvas>().sortingOrder = 100;
                ClickTracker overlayTracker = CreateClickableElement(
                    "PhysicsOverlayTarget", Vector2.zero, new Vector2(200f, 100f));
                yield return null;

                Vector2 screenPos = GetScreenPosition(overlayTracker.gameObject);
                GameObject physicsTarget = new GameObject("Physics2DTarget");
                physicsTarget.transform.SetParent(physicsRoot.transform, false);
                physicsTarget.transform.position = GetWorldPointOnPhysicsPlane(physicsCamera, screenPos);
                BoxCollider2D collider = physicsTarget.AddComponent<BoxCollider2D>();
                collider.size = new Vector2(2f, 2f);
                ClickTracker physicsTracker = physicsTarget.AddComponent<ClickTracker>();
                yield return null;

                PointerEventData pointerData = new PointerEventData(EventSystem.current)
                {
                    position = screenPos
                };
                List<RaycastResult> raycastResults = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, raycastResults);

                Assert.IsNotEmpty(raycastResults, "Setup: the Physics2D raycaster should hit.");
                Assert.AreEqual(physicsTarget, raycastResults[0].gameObject,
                    "Setup: EventSystem should expose the prioritized Physics2D hit first.");
                Assert.IsTrue(
                    raycastResults.Exists(result => result.gameObject == overlayTracker.gameObject),
                    "Setup: overlay UI should also be a normal EventSystem hit.");

                yield return RunTool(new JObject
                {
                    ["action"] = MouseAction.Click.ToString(),
                    ["x"] = screenPos.x,
                    ["y"] = screenPos.y
                });

                Assert.IsTrue(lastResponse.Success);
                Assert.IsTrue(overlayTracker.PointerClickCalled, "Overlay UI should receive the click");
                Assert.IsFalse(physicsTracker.PointerClickCalled, "Physics2D target behind overlay UI should not receive the click");
                Assert.AreEqual("PhysicsOverlayTarget", lastResponse.HitGameObjectName);
            }
            finally
            {
                Object.Destroy(physicsRoot);
            }
        }

        // Forces the remaining ordering shape from issue 1317: EventSystem reports a
        // lower-priority GraphicRaycaster hit while the front overlay is clipped from
        // EventSystem results, so the tool must compare the Canvas-space candidate.
        [UnityTest]
        public IEnumerator Click_Should_PreferClippedHigherOrderOverlayUiOverLowerGraphicRaycasterHit()
        {
            GameObject lowerCameraGo = new GameObject("LowerGraphicCamera");
            GameObject lowerCanvasGo = new GameObject("LowerGraphicCanvas");

            try
            {
                lowerCameraGo.transform.position = new Vector3(0f, 0f, -10f);
                Camera lowerCamera = lowerCameraGo.AddComponent<Camera>();
                lowerCamera.orthographic = true;

                Canvas lowerCanvas = lowerCanvasGo.AddComponent<Canvas>();
                lowerCanvas.renderMode = RenderMode.ScreenSpaceCamera;
                lowerCanvas.worldCamera = lowerCamera;
                lowerCanvas.sortingOrder = 0;

                GameObject lowerMask = CreateChildUIElement(
                    "LowerGraphicMask", lowerCanvasGo.transform, Vector2.zero, new Vector2(2000f, 2000f));
                lowerMask.AddComponent<Image>();
                ClickTracker lowerTracker = lowerMask.AddComponent<ClickTracker>();
                AlwaysHitGraphicRaycaster lowerRaycaster = lowerCanvasGo.AddComponent<AlwaysHitGraphicRaycaster>();
                lowerRaycaster.Target = lowerMask;

                canvasGo.GetComponent<Canvas>().sortingOrder = 100;
                Vector2 clippedOverlayOffset = new Vector2(Screen.width, 0f);
                ClickTracker overlayTracker = CreateClickableElement(
                    "FrontOverlayButton", clippedOverlayOffset, new Vector2(200f, 100f));
                yield return null;

                Vector2 screenPos = GetScreenPosition(overlayTracker.gameObject);
                PointerEventData pointerData = new PointerEventData(EventSystem.current)
                {
                    position = screenPos
                };
                List<RaycastResult> raycastResults = new List<RaycastResult>();
                EventSystem.current.RaycastAll(pointerData, raycastResults);

                Assert.IsNotEmpty(raycastResults, "Setup: the lower GraphicRaycaster should hit.");
                Assert.AreEqual(lowerMask, raycastResults[0].gameObject,
                    "Setup: EventSystem should expose the lower-priority GraphicRaycaster hit first.");
                Assert.IsFalse(
                    raycastResults.Exists(result => result.gameObject == overlayTracker.gameObject),
                    "Setup: overlay UI must be clipped out of EventSystem results for this regression test.");

                yield return RunTool(new JObject
                {
                    ["action"] = MouseAction.Click.ToString(),
                    ["x"] = screenPos.x,
                    ["y"] = screenPos.y
                });

                Assert.IsTrue(lastResponse.Success);
                Assert.IsTrue(overlayTracker.PointerClickCalled, "Higher-order overlay UI should receive the click");
                Assert.IsFalse(lowerTracker.PointerClickCalled, "Lower GraphicRaycaster target should not receive the click");
                Assert.AreEqual("FrontOverlayButton", lastResponse.HitGameObjectName);
            }
            finally
            {
                Object.Destroy(lowerCanvasGo);
                Object.Destroy(lowerCameraGo);
            }
        }

        [UnityTest]
        public IEnumerator Click_WithBypassRaycast_Should_UseTargetPathWhenNamesDuplicate()
        {
            GameObject firstPanel = CreateUIElement("FirstPanel", new Vector2(-120f, 0f), new Vector2(240f, 160f));
            GameObject secondPanel = CreateUIElement("SecondPanel", new Vector2(120f, 0f), new Vector2(240f, 160f));
            ClickTracker firstTracker = CreateChildClickableElement("SharedButton", firstPanel.transform, Vector2.zero, new Vector2(200f, 100f));
            ClickTracker secondTracker = CreateChildClickableElement("SharedButton", secondPanel.transform, Vector2.zero, new Vector2(200f, 100f));
            yield return null;

            Vector2 screenPos = GetScreenPosition(firstTracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Click.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y,
                ["bypassRaycast"] = true,
                ["targetPath"] = "TestCanvas/SecondPanel/SharedButton"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsFalse(firstTracker.PointerClickCalled, "First duplicate should not be clicked");
            Assert.IsTrue(secondTracker.PointerClickCalled, "Second duplicate should be clicked");
            Assert.AreEqual("SharedButton", lastResponse.HitGameObjectName);
        }

        [UnityTest]
        public IEnumerator Click_WithBypassRaycast_Should_FailWhenTargetPathIsAmbiguous()
        {
            GameObject panel = CreateUIElement("Panel", Vector2.zero, new Vector2(260f, 160f));
            ClickTracker firstTracker = CreateChildClickableElement("SharedButton", panel.transform, new Vector2(-40f, 0f), new Vector2(100f, 80f));
            ClickTracker secondTracker = CreateChildClickableElement("SharedButton", panel.transform, new Vector2(40f, 0f), new Vector2(100f, 80f));
            yield return null;

            Vector2 screenPos = GetScreenPosition(firstTracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Click.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y,
                ["bypassRaycast"] = true,
                ["targetPath"] = "TestCanvas/Panel/SharedButton"
            });

            Assert.IsFalse(lastResponse.Success);
            Assert.IsFalse(firstTracker.PointerClickCalled, "Ambiguous target path should not click the first match");
            Assert.IsFalse(secondTracker.PointerClickCalled, "Ambiguous target path should not click the second match");
            StringAssert.Contains("matched 2 active GameObjects", lastResponse.Message);
        }

        #endregion

        #region LongPress Tests

        [UnityTest]
        public IEnumerator LongPress_WithBypassRaycast_Should_HoldTargetBehindBlocker()
        {
            ClickTracker tracker = CreateClickableElement("LongPressTarget", Vector2.zero, new Vector2(200f, 100f));
            GameObject blocker = CreateUIElement("Blocker", Vector2.zero, new Vector2(260f, 160f));
            blocker.AddComponent<Image>();
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.LongPress.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y,
                ["duration"] = 0.1f,
                ["bypassRaycast"] = true,
                ["targetPath"] = "TestCanvas/LongPressTarget"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.PointerDownCalled, "PointerDown should be fired");
            Assert.IsTrue(tracker.PointerUpCalled, "PointerUp should be fired");
            Assert.IsFalse(tracker.PointerClickCalled, "LongPress should not fire PointerClick");
            Assert.AreEqual("LongPressTarget", lastResponse.HitGameObjectName);
        }

        #endregion

        #region DragOneShot Tests

        [UnityTest]
        public IEnumerator DragOneShot_Should_FireAllDragEvents()
        {
            DragTracker tracker = CreateDraggableElement("DragTarget", Vector2.zero, new Vector2(200, 100));
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);
            float destX = screenPos.x + 100f;
            float destY = screenPos.y;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Drag.ToString(),
                ["fromX"] = screenPos.x,
                ["fromY"] = screenPos.y,
                ["x"] = destX,
                ["y"] = destY,
                ["dragSpeed"] = 1000f
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.BeginDragCalled, "BeginDrag should be fired");
            Assert.IsTrue(tracker.DragCallCount >= 1, "At least one drag event should be fired");
            Assert.IsTrue(tracker.EndDragCalled, "EndDrag should be fired");
            Assert.AreEqual("DragTarget", lastResponse.HitGameObjectName);
        }

        [UnityTest]
        public IEnumerator DragOneShot_AtEmptyPosition_Should_ReturnFailure()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Drag.ToString(),
                ["fromX"] = 1,
                ["fromY"] = 1,
                ["x"] = 100,
                ["y"] = 100,
                ["dragSpeed"] = 1000f
            });

            Assert.IsFalse(lastResponse.Success);
            Assert.IsNull(lastResponse.HitGameObjectName);
        }

        [UnityTest]
        public IEnumerator DragOneShot_WithZeroSpeed_Should_CompleteInMinimalFrames()
        {
            DragTracker tracker = CreateDraggableElement("DragTarget", Vector2.zero, new Vector2(200, 100));
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);
            float destX = screenPos.x + 100f;
            float destY = screenPos.y;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Drag.ToString(),
                ["fromX"] = screenPos.x,
                ["fromY"] = screenPos.y,
                ["x"] = destX,
                ["y"] = destY,
                ["dragSpeed"] = 0f
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.BeginDragCalled, "BeginDrag should be fired");
            Assert.AreEqual(1, tracker.DragCallCount, "Exactly one drag event should be fired for instant drag");
            Assert.IsTrue(tracker.EndDragCalled, "EndDrag should be fired");
        }

        [UnityTest]
        public IEnumerator DragOneShot_Should_EndAtExactPosition()
        {
            DragTracker tracker = CreateDraggableElement("DragTarget", Vector2.zero, new Vector2(200, 100));
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);
            Vector2 endScreenPos = screenPos + new Vector2(150f, 50f);

            // simulate-mouse uses top-left origin; convert from Unity screen space (bottom-left origin)
            Vector2 startInputPos = ScreenToInput(screenPos);
            Vector2 endInputPos = ScreenToInput(endScreenPos);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Drag.ToString(),
                ["fromX"] = startInputPos.x,
                ["fromY"] = startInputPos.y,
                ["x"] = endInputPos.x,
                ["y"] = endInputPos.y,
                ["dragSpeed"] = 1000f
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual(endScreenPos, tracker.LastDragPosition, "Final drag position should match end position exactly");
        }

        [UnityTest]
        public IEnumerator DragOneShot_WithBypassRaycast_Should_DragTargetBehindBlocker()
        {
            DragTracker tracker = CreateDraggableElement("DragTarget", Vector2.zero, new Vector2(200f, 100f));
            GameObject blocker = CreateUIElement("Blocker", Vector2.zero, new Vector2(260f, 160f));
            blocker.AddComponent<Image>();
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Drag.ToString(),
                ["fromX"] = screenPos.x,
                ["fromY"] = screenPos.y,
                ["x"] = screenPos.x + 100f,
                ["y"] = screenPos.y,
                ["dragSpeed"] = 0f,
                ["bypassRaycast"] = true,
                ["targetPath"] = "TestCanvas/DragTarget"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.BeginDragCalled, "BeginDrag should be fired");
            Assert.AreEqual(1, tracker.DragCallCount, "Exactly one drag event should be fired for instant drag");
            Assert.IsTrue(tracker.EndDragCalled, "EndDrag should be fired");
            Assert.AreEqual("DragTarget", lastResponse.HitGameObjectName);
        }

        [UnityTest]
        public IEnumerator DragOneShot_WithBypassRaycast_Should_DropOnTargetPathBehindBlocker()
        {
            DragTracker dragTracker = CreateDraggableElement("DragTarget", new Vector2(-120f, 0f), new Vector2(100f, 80f));
            DropTracker dropTracker = CreateDropTarget("DropTarget", new Vector2(120f, 0f), new Vector2(120f, 90f));
            GameObject blocker = CreateUIElement("Blocker", Vector2.zero, new Vector2(400f, 180f));
            blocker.AddComponent<Image>();
            yield return null;

            Vector2 startPos = GetScreenPosition(dragTracker.gameObject);
            Vector2 endPos = GetScreenPosition(dropTracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.Drag.ToString(),
                ["fromX"] = startPos.x,
                ["fromY"] = startPos.y,
                ["x"] = endPos.x,
                ["y"] = endPos.y,
                ["dragSpeed"] = 0f,
                ["bypassRaycast"] = true,
                ["targetPath"] = "TestCanvas/DragTarget",
                ["dropTargetPath"] = "TestCanvas/DropTarget"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(dropTracker.DropCalled, "Drop should be fired on the explicit drop target");
        }

        #endregion

        #region Split Drag Tests

        [UnityTest]
        public IEnumerator DragSplit_Should_CompleteFullCycle()
        {
            DragTracker tracker = CreateDraggableElement("DragTarget", Vector2.zero, new Vector2(200, 100));
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragStart.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.BeginDragCalled, "BeginDrag should be fired");
            Assert.AreEqual("DragTarget", lastResponse.HitGameObjectName);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragMove.ToString(),
                ["x"] = screenPos.x + 50f,
                ["y"] = screenPos.y
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.DragCallCount >= 1, "At least one drag event should be fired");

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragEnd.ToString(),
                ["x"] = screenPos.x + 100f,
                ["y"] = screenPos.y
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.EndDragCalled, "EndDrag should be fired");
        }

        [UnityTest]
        public IEnumerator DragStart_WhenAlreadyDragging_Should_ReturnFailure()
        {
            DragTracker tracker = CreateDraggableElement("DragTarget", Vector2.zero, new Vector2(200, 100));
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragStart.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y
            });
            Assert.IsTrue(lastResponse.Success);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragStart.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y
            });
            Assert.IsFalse(lastResponse.Success);
        }

        [UnityTest]
        public IEnumerator DragMove_WhenNotDragging_Should_ReturnFailure()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragMove.ToString(),
                ["x"] = 100,
                ["y"] = 100
            });

            Assert.IsFalse(lastResponse.Success);
        }

        [UnityTest]
        public IEnumerator DragEnd_WhenNotDragging_Should_ReturnFailure()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragEnd.ToString(),
                ["x"] = 100,
                ["y"] = 100
            });

            Assert.IsFalse(lastResponse.Success);
        }

        [UnityTest]
        public IEnumerator DragMove_Should_InterpolateAtSpeed()
        {
            yield return StartDragOnNewElement();
            lastDragTracker.DragCallCount = 0;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragMove.ToString(),
                ["x"] = lastDragScreenPos.x + 100f,
                ["y"] = lastDragScreenPos.y,
                ["dragSpeed"] = 1000f
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(lastDragTracker.DragCallCount >= 1, "At least one drag event should be fired during interpolation");

            yield return EndDragInstant(lastDragScreenPos.x + 100f, lastDragScreenPos.y);
        }

        [UnityTest]
        public IEnumerator DragMove_WithZeroSpeed_Should_MoveInstantly()
        {
            yield return StartDragOnNewElement();
            lastDragTracker.DragCallCount = 0;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragMove.ToString(),
                ["x"] = lastDragScreenPos.x + 100f,
                ["y"] = lastDragScreenPos.y,
                ["dragSpeed"] = 0f
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual(1, lastDragTracker.DragCallCount, "Exactly one drag event should be fired for instant move");

            yield return EndDragInstant(lastDragScreenPos.x + 100f, lastDragScreenPos.y);
        }

        [UnityTest]
        public IEnumerator DragEnd_Should_InterpolateBeforeRelease()
        {
            yield return StartDragOnNewElement();
            lastDragTracker.DragCallCount = 0;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragEnd.ToString(),
                ["x"] = lastDragScreenPos.x + 100f,
                ["y"] = lastDragScreenPos.y,
                ["dragSpeed"] = 1000f
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(lastDragTracker.DragCallCount >= 1, "Drag events should be fired during interpolation before EndDrag");
            Assert.IsTrue(lastDragTracker.EndDragCalled, "EndDrag should be fired after interpolation");
        }

        [UnityTest]
        public IEnumerator DragStart_WithBypassRaycast_Should_StartTargetBehindBlocker()
        {
            DragTracker tracker = CreateDraggableElement("DragTarget", Vector2.zero, new Vector2(200f, 100f));
            GameObject blocker = CreateUIElement("Blocker", Vector2.zero, new Vector2(260f, 160f));
            blocker.AddComponent<Image>();
            yield return null;

            Vector2 screenPos = GetScreenPosition(tracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragStart.ToString(),
                ["x"] = screenPos.x,
                ["y"] = screenPos.y,
                ["bypassRaycast"] = true,
                ["targetPath"] = "TestCanvas/DragTarget"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(tracker.BeginDragCalled, "BeginDrag should be fired");
            Assert.AreEqual("DragTarget", lastResponse.HitGameObjectName);

            yield return EndDragInstant(screenPos.x + 100f, screenPos.y);
            Assert.IsTrue(tracker.EndDragCalled, "EndDrag should be fired");
        }

        [UnityTest]
        public IEnumerator DragStart_AtEmptyPosition_Should_ReturnFailure()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragStart.ToString(),
                ["x"] = 1,
                ["y"] = 1
            });

            Assert.IsFalse(lastResponse.Success);
        }

        #endregion

        #region Helpers

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

        private ClickTracker CreateClickableElement(string name, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject go = CreateUIElement(name, anchoredPosition, sizeDelta);
            go.AddComponent<Image>();
            return go.AddComponent<ClickTracker>();
        }

        private ClickTracker CreateChildClickableElement(string name, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject go = CreateChildUIElement(name, parent, anchoredPosition, sizeDelta);
            go.AddComponent<Image>();
            return go.AddComponent<ClickTracker>();
        }

        private DragTracker CreateDraggableElement(string name, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject go = CreateUIElement(name, anchoredPosition, sizeDelta);
            go.AddComponent<Image>();
            return go.AddComponent<DragTracker>();
        }

        private DropTracker CreateDropTarget(string name, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject go = CreateUIElement(name, anchoredPosition, sizeDelta);
            go.AddComponent<Image>();
            return go.AddComponent<DropTracker>();
        }

        private GameObject CreateUIElement(string name, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            return CreateChildUIElement(name, canvasGo.transform, anchoredPosition, sizeDelta);
        }

        private GameObject CreateChildUIElement(string name, Transform parent, Vector2 anchoredPosition, Vector2 sizeDelta)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            RectTransform rect = go.AddComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 0.5f);
            rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = sizeDelta;
            return go;
        }

        private Vector2 GetScreenPosition(GameObject go)
        {
            return (Vector2)go.GetComponent<RectTransform>().position;
        }

        private Vector3 GetWorldPointOnPhysicsPlane(Camera physicsCamera, Vector2 screenPos)
        {
            Vector3 screenPoint = new Vector3(screenPos.x, screenPos.y, -physicsCamera.transform.position.z);
            Vector3 worldPoint = physicsCamera.ScreenToWorldPoint(screenPoint);
            worldPoint.z = 0f;
            return worldPoint;
        }

        // simulate-mouse uses top-left origin; Unity screen space uses bottom-left origin
        private Vector2 ScreenToInput(Vector2 screenPos)
        {
            return new Vector2(screenPos.x, Screen.height - screenPos.y);
        }

        private DragTracker lastDragTracker = null!;
        private Vector2 lastDragScreenPos;

        private IEnumerator StartDragOnNewElement()
        {
            lastDragTracker = CreateDraggableElement("DragTarget", Vector2.zero, new Vector2(200, 100));
            yield return null;

            lastDragScreenPos = GetScreenPosition(lastDragTracker.gameObject);

            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragStart.ToString(),
                ["x"] = lastDragScreenPos.x,
                ["y"] = lastDragScreenPos.y
            });
            Assert.IsTrue(lastResponse.Success);
        }

        private IEnumerator EndDragInstant(float x, float y)
        {
            yield return RunTool(new JObject
            {
                ["action"] = MouseAction.DragEnd.ToString(),
                ["x"] = x,
                ["y"] = y,
                ["dragSpeed"] = 0f
            });
            Assert.IsTrue(lastResponse.Success);
        }

        #endregion
    }

    // Tracks pointer click events for testing
    // Stands in for a non-UI raycaster (e.g. PhysicsRaycaster) without requiring the
    // physics modules: always reports a hit on Target, ignoring Screen-bounds clipping.
    public class AlwaysHitRaycaster : BaseRaycaster
    {
        public GameObject Target = null!;

        public override Camera eventCamera => null!;

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            resultAppendList.Add(new RaycastResult
            {
                gameObject = Target,
                module = this,
                distance = 0f,
                screenPosition = eventData.position
            });
        }
    }

    // Stands in for a lower-priority GraphicRaycaster that still reports an
    // EventSystem hit when the front overlay UI is clipped by Screen bounds.
    public class AlwaysHitGraphicRaycaster : GraphicRaycaster
    {
        public GameObject Target = null!;

        public override void Raycast(PointerEventData eventData, List<RaycastResult> resultAppendList)
        {
            Canvas canvas = GetComponent<Canvas>();
            resultAppendList.Add(new RaycastResult
            {
                gameObject = Target,
                module = this,
                distance = 0f,
                screenPosition = eventData.position,
                sortingLayer = canvas.sortingLayerID,
                sortingOrder = canvas.sortingOrder,
                depth = 0
            });
        }
    }

    public class HighPriorityPhysics2DRaycaster : Physics2DRaycaster
    {
        public override int sortOrderPriority => int.MaxValue;
        public override int renderOrderPriority => int.MaxValue;
    }

    public class ClickTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        public bool PointerDownCalled { get; private set; }
        public bool PointerUpCalled { get; private set; }
        public bool PointerClickCalled { get; private set; }

        public void OnPointerDown(PointerEventData eventData) { PointerDownCalled = true; }
        public void OnPointerUp(PointerEventData eventData) { PointerUpCalled = true; }
        public void OnPointerClick(PointerEventData eventData) { PointerClickCalled = true; }
    }

    // Tracks drag events and moves the element for testing
    public class DragTracker : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public bool BeginDragCalled { get; private set; }
        public bool EndDragCalled { get; private set; }
        public int DragCallCount { get; set; }
        public Vector2 LastDragPosition { get; private set; }

        private RectTransform rectTransform = null!;
        private Canvas canvas = null!;

        private void Awake()
        {
            rectTransform = GetComponent<RectTransform>();
            canvas = GetComponentInParent<Canvas>();
        }

        public void OnBeginDrag(PointerEventData eventData) { BeginDragCalled = true; }

        public void OnDrag(PointerEventData eventData)
        {
            DragCallCount++;
            LastDragPosition = eventData.position;
            rectTransform.anchoredPosition += eventData.delta / canvas.scaleFactor;
        }

        public void OnEndDrag(PointerEventData eventData) { EndDragCalled = true; }
    }

    public class DropTracker : MonoBehaviour, IDropHandler
    {
        public bool DropCalled { get; private set; }

        public void OnDrop(PointerEventData eventData) { DropCalled = true; }
    }
}
