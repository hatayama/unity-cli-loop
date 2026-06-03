#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Test fixture that verifies Simulate Mouse Input behavior.
    /// </summary>
    public class SimulateMouseInputTests : InputTestFixture
    {
        private SimulateMouseInputTool tool = null!;
        private SimulateMouseInputResponse lastResponse = null!;
        private Mouse mouse = null!;
        private GameObject mouseObserverGo = null!;
        private MouseUpdateFramePressObserver mouseUpdateFramePressObserver = null!;

        public override void Setup()
        {
            base.Setup();
            tool = new SimulateMouseInputTool();
            mouse = InputSystem.AddDevice<Mouse>();
            mouseObserverGo = new GameObject("MouseUpdateFramePressObserver");
            mouseUpdateFramePressObserver = mouseObserverGo.AddComponent<MouseUpdateFramePressObserver>();
        }

        public override void TearDown()
        {
            InputSystemUpdateHelper.ResetPauseProviderForTests();
            UloopPausePointRegistry.ResetForTests();
            MouseInputState.ReleaseAllButtons();
            Object.DestroyImmediate(mouseObserverGo);
            base.TearDown();
        }

        #region Click Tests

        [UnityTest]
        public IEnumerator Click_Should_SetWasPressedThisFrame()
        {
            // Verifies that Click is visible to gameplay Update polling through wasPressedThisFrame.
            yield return null;

            mouseUpdateFramePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.Click.ToString(),
                ["x"] = 400,
                ["y"] = 300
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("Click", lastResponse.Action);
            Assert.AreEqual("Left", lastResponse.Button);
            Assert.Greater(mouseUpdateFramePressObserver.LeftButtonPressedUpdateCount, 0, "Click should be visible to MonoBehaviour.Update via wasPressedThisFrame");
            // After click completes, button should be released
            Assert.IsFalse(mouse.leftButton.isPressed, "Left button should be released after click");
        }

        [UnityTest]
        public IEnumerator Click_RightButton_Should_InjectRightClick()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.Click.ToString(),
                ["x"] = 400,
                ["y"] = 300,
                ["button"] = MouseButton.Right.ToString()
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("Right", lastResponse.Button);
            Assert.IsFalse(mouse.rightButton.isPressed, "Right button should be released after click");
        }

        [UnityTest]
        public IEnumerator Click_MiddleButton_Should_InjectMiddleClick()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.Click.ToString(),
                ["x"] = 400,
                ["y"] = 300,
                ["button"] = MouseButton.Middle.ToString()
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("Middle", lastResponse.Button);
            Assert.IsFalse(mouse.middleButton.isPressed, "Middle button should be released after click");
        }

        [UnityTest]
        public IEnumerator Click_WhenUnityPausesDuringObservation_Should_CompleteAsDebugBreakInterruption()
        {
            // Verifies that a debug-break pause releases the tool slot instead of leaving the click command busy.
            yield return null;

            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(new JObject
            {
                ["action"] = MouseInputAction.Click.ToString(),
                ["x"] = 400,
                ["y"] = 300,
                ["duration"] = 1f
            }, System.Threading.CancellationToken.None);

            yield return new WaitUntil(() => mouse.leftButton.isPressed || task.IsCompleted);
            Assert.IsFalse(task.IsCompleted, "The test must pause during the click observation window.");

            InputSystemUpdateHelper.ConfigurePauseProviderForTests(() => true);
            yield return WaitForTask(task);
            InputSystemUpdateHelper.ResetPauseProviderForTests();

            lastResponse = (SimulateMouseInputResponse)task.Result;
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(lastResponse.InterruptedByDebugBreak);
            Assert.AreEqual("Click", lastResponse.Action);
            Assert.AreEqual("Left", lastResponse.Button);
            Assert.IsNull(lastResponse.DebugBreakId);
            Assert.IsNull(lastResponse.DebugBreakHitCount);
            Assert.IsFalse(mouse.leftButton.isPressed, "Debug-break interruption should release the injected mouse button state.");
            Assert.IsFalse(SimulateMouseInputOverlayState.HasAnyActivity, "Debug-break interruption should clear mouse overlay state.");
        }

        [UnityTest]
        public IEnumerator Click_WhenDebugBreakMarkerHits_Should_ReturnMarkerDetails()
        {
            // Verifies marker-caused interruption reports the marker id and hit count.
            yield return null;

            UloopPausePointRegistry.ConfigureForTests(
                new FakePausePointPauseController(),
                () => new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc));
            UloopPausePointRegistry.Enable("left-click", 30);
            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(new JObject
            {
                ["action"] = MouseInputAction.Click.ToString(),
                ["x"] = 400,
                ["y"] = 300,
                ["duration"] = 1f
            }, System.Threading.CancellationToken.None);

            yield return new WaitUntil(() => mouse.leftButton.isPressed || task.IsCompleted);
            Assert.IsFalse(task.IsCompleted, "The test must pause during the click observation window.");

            UnityCliLoopDebug.Break("left-click");
            InputSystemUpdateHelper.ConfigurePauseProviderForTests(() => true);
            yield return WaitForTask(task);
            InputSystemUpdateHelper.ResetPauseProviderForTests();

            lastResponse = (SimulateMouseInputResponse)task.Result;
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(lastResponse.InterruptedByDebugBreak);
            Assert.AreEqual("left-click", lastResponse.DebugBreakId);
            Assert.AreEqual(1, lastResponse.DebugBreakHitCount);
            Assert.IsFalse(mouse.leftButton.isPressed, "Marker interruption should release the injected mouse button state.");
        }

        #endregion

        #region LongPress Tests

        [UnityTest]
        public IEnumerator LongPress_Should_HoldButtonForDuration()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.LongPress.ToString(),
                ["x"] = 400,
                ["y"] = 300,
                ["duration"] = 0.1f
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("LongPress", lastResponse.Action);
            // After long press completes, button should be released
            Assert.IsFalse(mouse.leftButton.isPressed, "Button should be released after long press");
        }

        [UnityTest]
        public IEnumerator LongPress_WithZeroDuration_Should_ReturnError()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.LongPress.ToString(),
                ["x"] = 400,
                ["y"] = 300,
                ["duration"] = 0f
            });

            Assert.IsFalse(lastResponse.Success, "LongPress with zero duration should fail");
        }

        [UnityTest]
        public IEnumerator LongPress_Should_RestoreRunInBackground_WhenOriginallyDisabled()
        {
            // Verifies that mouse input simulation keeps PlayMode running in the background only during execution.
            yield return null;

            bool originalRunInBackground = UnityEngine.Application.runInBackground;

            try
            {
                UnityEngine.Application.runInBackground = false;
                Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(new JObject
                {
                    ["action"] = MouseInputAction.LongPress.ToString(),
                    ["x"] = 400,
                    ["y"] = 300,
                    ["duration"] = 0.2f
                }, System.Threading.CancellationToken.None);

                float toggleTimeoutAt = Time.realtimeSinceStartup + 2f;
                yield return new WaitUntil(() =>
                    UnityEngine.Application.runInBackground
                    || task.IsCompleted
                    || Time.realtimeSinceStartup >= toggleTimeoutAt);
                Assert.IsTrue(
                    UnityEngine.Application.runInBackground,
                    "Mouse input simulation should enable Run In Background while executing.");

                float completionTimeoutAt = Time.realtimeSinceStartup + 5f;
                yield return new WaitUntil(() =>
                    task.IsCompleted || Time.realtimeSinceStartup >= completionTimeoutAt);
                Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
                Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");

                lastResponse = (SimulateMouseInputResponse)task.Result;
                Assert.IsTrue(lastResponse.Success);
                Assert.IsFalse(
                    UnityEngine.Application.runInBackground,
                    "Mouse input simulation should restore the original Run In Background value.");
            }
            finally
            {
                UnityEngine.Application.runInBackground = originalRunInBackground;
            }
        }

        #endregion

        #region MoveDelta Tests

        [UnityTest]
        public IEnumerator MoveDelta_Should_InjectDelta()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.MoveDelta.ToString(),
                ["deltaX"] = 100f,
                ["deltaY"] = -50f
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("MoveDelta", lastResponse.Action);
        }

        #endregion

        #region Scroll Tests

        [UnityTest]
        public IEnumerator Scroll_Should_InjectScrollDelta()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.Scroll.ToString(),
                ["scrollY"] = 120f
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("Scroll", lastResponse.Action);
        }

        [UnityTest]
        public IEnumerator Scroll_Horizontal_Should_InjectScrollX()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.Scroll.ToString(),
                ["scrollX"] = 120f
            });

            Assert.IsTrue(lastResponse.Success);
        }

        #endregion

        #region Helpers

        private IEnumerator RunTool(JObject parameters)
        {
            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(parameters, System.Threading.CancellationToken.None);
            yield return WaitForTask(task);
            lastResponse = (SimulateMouseInputResponse)task.Result;
        }

        private static IEnumerator WaitForTask(Task<UnityCliLoopToolResponse> task)
        {
            float timeoutAt = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() =>
                task.IsCompleted || Time.realtimeSinceStartup >= timeoutAt);
            Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
            Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");
        }

        /// <summary>
        /// Records pause requests without pausing the real Unity Editor.
        /// </summary>
        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused { get; private set; }

            public void Pause()
            {
                IsPaused = true;
            }
        }

        #endregion
    }

    /// <summary>
    /// Test support type used by play mode mouse input fixtures.
    /// </summary>
    public class MouseUpdateFramePressObserver : MonoBehaviour
    {
        public int LeftButtonPressedUpdateCount { get; private set; }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null)
            {
                return;
            }

            if (mouse.leftButton.wasPressedThisFrame)
            {
                LeftButtonPressedUpdateCount++;
            }
        }

        public void ResetCount()
        {
            LeftButtonPressedUpdateCount = 0;
        }
    }
}
#endif
