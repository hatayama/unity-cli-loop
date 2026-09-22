#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Verifies simulate-keyboard stays assertion-free and warns when an input callback disables its own action map.
    /// </summary>
    public class SimulateKeyboardMonitorRemovalTests : InputTestFixture
    {
        private TestableSimulateKeyboardTool tool = null!;
        private SimulateKeyboardResponse lastResponse = null!;
        private int assertLogCount;

        public override void Setup()
        {
            base.Setup();
            tool = new TestableSimulateKeyboardTool();
            InputSystem.AddDevice<Keyboard>();
            assertLogCount = 0;
            UnityEngine.Application.logMessageReceived += CountAssertLog;
        }

        public override void TearDown()
        {
            UnityEngine.Application.logMessageReceived -= CountAssertLog;
            DeferredPlayerLatchSynchronizer.ResetForTests();
            KeyboardKeyState.ReleaseAllKeys();
            SimulateKeyboardOverlayState.Clear();
            InputVisualizationCanvas[] canvases =
                Object.FindObjectsByType<InputVisualizationCanvas>(FindObjectsSortMode.None);
            for (int index = 0; index < canvases.Length; index++)
            {
                Object.DestroyImmediate(canvases[index].gameObject);
            }

            base.TearDown();
        }

        /// <summary>
        /// Verifies a performed callback disabling its own map during Press logs no assertion
        /// and the response carries the monitor-removal warning.
        /// </summary>
        [UnityTest]
        public IEnumerator Press_WhenPerformedDisablesOwnMap_LogsNoAssertionAndWarns()
        {
            InputActionMap map = new InputActionMap("MonitorRemovalRepro");
            InputAction action = map.AddAction("PressB", InputActionType.Button, "<Keyboard>/b");
            action.performed += _ => map.Disable();
            map.Enable();
            try
            {
                yield return null;

                yield return RunTool(new JObject
                {
                    ["action"] = KeyboardAction.Press.ToString(),
                    ["key"] = "B"
                });

                Assert.That(assertLogCount, Is.EqualTo(0));
                Assert.That(map.enabled, Is.False, "performed must have fired and disabled the map");
                Assert.That(lastResponse.Success, Is.True);
                Assert.That(
                    lastResponse.Warning,
                    Does.Contain(InputStateMonitorRemovalWarningBuilder.MonitorRemovalWarning));
            }
            finally
            {
                map.Disable();
            }
        }

        /// <summary>
        /// Verifies a release-only performed callback disabling its own map during ReleaseAll logs no assertion
        /// and the ReleaseAll response carries the monitor-removal warning.
        /// </summary>
        [UnityTest]
        public IEnumerator ReleaseAll_WhenReleaseOnlyPerformedDisablesOwnMap_LogsNoAssertionAndWarns()
        {
            InputActionMap map = new InputActionMap("MonitorRemovalReleaseRepro");
            InputAction action = map.AddAction(
                "ReleaseB",
                InputActionType.Button,
                "<Keyboard>/b",
                interactions: "press(behavior=1)");
            action.performed += _ => map.Disable();
            map.Enable();
            try
            {
                yield return null;

                yield return RunTool(new JObject
                {
                    ["action"] = KeyboardAction.KeyDown.ToString(),
                    ["key"] = "B"
                });
                Assert.That(lastResponse.Success, Is.True);
                Assert.That(map.enabled, Is.True, "release-only interaction must not perform on key down");

                yield return RunTool(new JObject
                {
                    ["action"] = UnityCliLoopKeyboardAction.ReleaseAll.ToString()
                });

                Assert.That(assertLogCount, Is.EqualTo(0));
                Assert.That(map.enabled, Is.False, "performed must have fired on release and disabled the map");
                Assert.That(lastResponse.Success, Is.True);
                Assert.That(
                    lastResponse.Warning,
                    Does.Contain(InputStateMonitorRemovalWarningBuilder.MonitorRemovalWarning));
            }
            finally
            {
                map.Disable();
            }
        }

        private void CountAssertLog(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Assert)
            {
                assertLogCount++;
            }
        }

        private IEnumerator RunTool(JObject parameters)
        {
            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(parameters, System.Threading.CancellationToken.None);
            float timeoutAt = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() =>
                task.IsCompleted || Time.realtimeSinceStartup >= timeoutAt);
            Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
            Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");
            lastResponse = (SimulateKeyboardResponse)task.Result;
        }
    }
}
#endif
