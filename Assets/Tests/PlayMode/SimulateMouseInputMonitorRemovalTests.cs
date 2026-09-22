#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Verifies simulate-mouse-input stays assertion-free and warns when an input callback disables its own action map.
    /// </summary>
    public class SimulateMouseInputMonitorRemovalTests : InputTestFixture
    {
        private SimulateMouseInputTool tool = null!;
        private SimulateMouseInputResponse lastResponse = null!;
        private int assertLogCount;

        public override void Setup()
        {
            base.Setup();
            tool = new SimulateMouseInputTool();
            InputSystem.AddDevice<Mouse>();
            assertLogCount = 0;
            UnityEngine.Application.logMessageReceived += CountAssertLog;
        }

        public override void TearDown()
        {
            UnityEngine.Application.logMessageReceived -= CountAssertLog;
            InputSystemUpdateHelper.ResetPauseProviderForTests();
            InputSystemUpdateHelper.ResetTimeoutsForTests();
            UloopPausePointRegistry.ResetForTests();
            MouseInputState.ReleaseAllButtons();
            base.TearDown();
        }

        /// <summary>
        /// Verifies a performed callback disabling its own map during Click logs no assertion
        /// and the response carries the monitor-removal warning.
        /// </summary>
        [UnityTest]
        public IEnumerator Click_WhenPerformedDisablesOwnMap_LogsNoAssertionAndWarns()
        {
            InputActionMap map = new InputActionMap("MouseMonitorRemovalRepro");
            InputAction action = map.AddAction("ClickLeft", InputActionType.Button, "<Mouse>/leftButton");
            action.performed += _ => map.Disable();
            map.Enable();
            try
            {
                yield return null;

                yield return RunTool(new JObject
                {
                    ["action"] = MouseInputAction.Click.ToString(),
                    ["x"] = 400,
                    ["y"] = 300
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
            lastResponse = (SimulateMouseInputResponse)task.Result;
        }
    }
}
#endif
