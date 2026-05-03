#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public class SimulateMouseInputTests : InputTestFixture
    {
        private SimulateMouseInputTool tool = null!;
        private SimulateMouseInputResponse lastResponse = null!;
        private Mouse mouse = null!;

        public override void Setup()
        {
            base.Setup();
            tool = new SimulateMouseInputTool();
            mouse = InputSystem.AddDevice<Mouse>();
        }

        public override void TearDown()
        {
            MouseInputState.ReleaseAllButtons();
            base.TearDown();
        }

        #region Click Tests

        [UnityTest]
        public IEnumerator Click_Should_SetWasPressedThisFrame()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = MouseInputAction.Click.ToString(),
                ["x"] = 400,
                ["y"] = 300
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("Click", lastResponse.Action);
            Assert.AreEqual("Left", lastResponse.Button);
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
            Task<BaseToolResponse> task = tool.ExecuteAsync(parameters);
            float timeoutAt = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() =>
                task.IsCompleted || Time.realtimeSinceStartup >= timeoutAt);
            Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
            Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");
            lastResponse = (SimulateMouseInputResponse)task.Result;
        }

        #endregion
    }
}
#endif
