#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections;
using System.Collections.Generic;
using System.Threading.Tasks;
using NUnit.Framework;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Verifies ReleaseAll/KeyUp responses report live device readback rather than bookkeeping-only success.
    /// </summary>
    public class SimulateKeyboardReleaseDeviceStateTests : InputTestFixture
    {
        private TestableSimulateKeyboardTool tool = null!;
        private SimulateKeyboardResponse lastResponse = null!;
        private Keyboard keyboard = null!;

        public override void Setup()
        {
            base.Setup();
            tool = new TestableSimulateKeyboardTool();
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        public override void TearDown()
        {
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
        /// Verifies ReleaseAllKeysImmediately readback includes the held key and matches a live
        /// device/update-type read taken immediately after the call, so a hardcoded bool or
        /// KeyUp-only wiring cannot satisfy the assertion.
        /// </summary>
        [UnityTest]
        public IEnumerator ReleaseAllKeysImmediately_AfterHeldKey_ReportsDeviceReadbackMatchingLiveDevice()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });
            Assert.IsTrue(lastResponse.Success);

            ReleaseAllKeysImmediateResult result =
                KeyboardInputMainThreadCleanup.ReleaseAllKeysImmediately(keyboard);
            bool liveDeviceIsPressed = keyboard[Key.W].isPressed;
            string liveUpdateType = InputState.currentUpdateType.ToString();

            ReleasedKeyState? wState = FindReleasedKeyState(result.ReleasedKeyStates, "W");
            Assert.That(wState, Is.Not.Null, "ReleaseAllKeysImmediately must report the key it released.");
            Assert.That(wState!.DeviceIsPressedAfterRelease, Is.EqualTo(liveDeviceIsPressed));
            Assert.That(result.KeyStateReadUpdateType, Is.Not.Empty);
            Assert.That(result.KeyStateReadUpdateType, Is.EqualTo(liveUpdateType));
            Assert.That(result.ReleasedKeys, Does.Contain("W"));
        }

        /// <summary>
        /// Verifies the ReleaseAll tool response copies ReleasedKeyStates and KeyStateReadUpdateType
        /// from the immediate-release path onto the wire response.
        /// </summary>
        [UnityTest]
        public IEnumerator ReleaseAll_AfterHeldKey_WiresReleasedKeyStatesOntoResponse()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "D"
            });
            Assert.IsTrue(lastResponse.Success);

            yield return RunTool(new JObject
            {
                ["action"] = UnityCliLoopKeyboardAction.ReleaseAll.ToString()
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.That(lastResponse.ReleasedKeys, Does.Contain("D"));
            Assert.That(lastResponse.ReleasedKeyStates, Is.Not.Null);
            ReleasedKeyState? dState = FindReleasedKeyState(lastResponse.ReleasedKeyStates!, "D");
            Assert.That(dState, Is.Not.Null, "ReleaseAll response must include device readback for D.");
            Assert.That(dState!.DeviceIsPressedAfterRelease, Is.EqualTo(keyboard[Key.D].isPressed));
            Assert.That(lastResponse.KeyStateReadUpdateType, Is.Not.Empty);
            Assert.That(lastResponse.KeyStateReadUpdateType, Is.EqualTo(InputState.currentUpdateType.ToString()));
            Assert.That(lastResponse.DeferredLatchSyncScheduled, Is.True);
            Assert.That(
                lastResponse.Message,
                Is.EqualTo(
                    "Released 1 key(s): D A deferred latch sync will run on the next player input update."));
        }

        /// <summary>
        /// Verifies ReleaseAll with no held keys returns empty ReleasedKeys and empty ReleasedKeyStates.
        /// </summary>
        [UnityTest]
        public IEnumerator ReleaseAll_WhenNothingHeld_ReturnsEmptyReleasedKeysAndStates()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = UnityCliLoopKeyboardAction.ReleaseAll.ToString()
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.That(lastResponse.ReleasedKeys, Is.Empty);
            Assert.That(lastResponse.ReleasedKeyStates, Is.Empty);
            Assert.That(lastResponse.DeferredLatchSyncScheduled, Is.False);
        }

        /// <summary>
        /// Verifies a successful KeyUp reports tracker and device key state on the response object.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyUp_WhenHeld_ReportsTrackedAndDeviceKeyState()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });
            Assert.IsTrue(lastResponse.Success);

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyUp.ToString(),
                ["key"] = "W"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.That(lastResponse.KeyStateTrackedHeld, Is.EqualTo(KeyboardKeyState.IsKeyHeld(Key.W)));
            Assert.That(lastResponse.KeyStateDeviceIsPressed, Is.Not.Null);
            Assert.That(lastResponse.KeyStateDeviceIsPressed, Is.EqualTo(keyboard[Key.W].isPressed));
            Assert.That(lastResponse.DeferredLatchSyncScheduled, Is.True);
        }

        /// <summary>
        /// Verifies KeyDown then ReleaseAll leaves the device unpressed after player update frames,
        /// including the deferred latch-sync path.
        /// </summary>
        [UnityTest]
        public IEnumerator ReleaseAll_AfterHeldKey_DeviceIsUnpressedAfterPlayerUpdateFrames()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "D"
            });
            Assert.IsTrue(lastResponse.Success);

            yield return RunTool(new JObject
            {
                ["action"] = UnityCliLoopKeyboardAction.ReleaseAll.ToString()
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.That(lastResponse.DeferredLatchSyncScheduled, Is.True);

            for (int frame = 0; frame < 5; frame++)
            {
                InputSystem.Update();
                yield return null;
            }

            Assert.That(keyboard[Key.D].isPressed, Is.False);
        }

        /// <summary>
        /// Verifies KeyDown after ReleaseAll of the same key is not ForceSync-released by the
        /// deferred latch callback: a tracked re-press must stay pressed across player updates.
        /// </summary>
        [UnityTest]
        public IEnumerator KeyDown_AfterReleaseAll_SameKeyStaysPressedAcrossPlayerUpdates()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "D"
            });
            Assert.IsTrue(lastResponse.Success);

            yield return RunTool(new JObject
            {
                ["action"] = UnityCliLoopKeyboardAction.ReleaseAll.ToString()
            });
            Assert.IsTrue(lastResponse.Success);

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "D"
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.That(keyboard[Key.D].isPressed, Is.True);
            Assert.That(KeyboardKeyState.IsKeyHeld(Key.D), Is.True);

            // Why re-arm: UnityTest frame pumps between ReleaseAll and the second KeyDown
            // already consume the ReleaseAll one-shot. Scheduling again while the tracker
            // holds is the stale-shaped callback the IsKeyHeld skip must ignore.
            bool scheduled = DeferredPlayerLatchSynchronizer.Schedule(new Key[] { Key.D });
            Assert.That(scheduled, Is.True);

            for (int frame = 0; frame < 5; frame++)
            {
                InputSystem.Update();
                yield return null;
            }

            Assert.That(keyboard[Key.D].isPressed, Is.True);
        }

        /// <summary>
        /// Verifies Schedule plus a player update ForceSyncs a device press that the tracker
        /// does not own, so deleting the onAfterUpdate subscription cannot satisfy this test.
        /// </summary>
        [UnityTest]
        public IEnumerator Schedule_WhenDevicePressedWithoutTracker_ForceSyncsUnpressedOnNextPlayerUpdate()
        {
            yield return null;

            InputSystem.QueueStateEvent(keyboard, new KeyboardState(Key.D));
            InputSystem.Update();
            Assert.That(keyboard[Key.D].isPressed, Is.True);
            Assert.That(KeyboardKeyState.IsKeyHeld(Key.D), Is.False);

            bool scheduled = DeferredPlayerLatchSynchronizer.Schedule(new Key[] { Key.D });
            Assert.That(scheduled, Is.True);

            InputSystem.Update();
            yield return null;

            Assert.That(keyboard[Key.D].isPressed, Is.False);
        }

        private IEnumerator RunTool(JObject parameters)
        {
            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(parameters, System.Threading.CancellationToken.None);
            yield return WaitForTask(task);
            lastResponse = (SimulateKeyboardResponse)task.Result;
        }

        private static IEnumerator WaitForTask(Task task)
        {
            float timeoutAt = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() =>
                task.IsCompleted || Time.realtimeSinceStartup >= timeoutAt);
            Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
            Assert.IsFalse(task.IsCanceled, "Tool execution should not be canceled.");
            Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");
        }

        private static ReleasedKeyState? FindReleasedKeyState(
            IReadOnlyList<ReleasedKeyState> states,
            string keyName)
        {
            for (int index = 0; index < states.Count; index++)
            {
                if (states[index].Key == keyName)
                {
                    return states[index];
                }
            }

            return null;
        }
    }

    /// <summary>
    /// Pins the still-pressed ReleaseAll note to a fixed wire sentence.
    /// </summary>
    public sealed class SimulateKeyboardReleaseMessageFormatterTests
    {
        /// <summary>
        /// Verifies the still-pressed note matches the exact English sentence callers will see.
        /// </summary>
        [Test]
        public void FormatStillPressedNote_WithCountAndUpdateType_ReturnsExactLiteral()
        {
            string note = SimulateKeyboardReleaseMessageFormatter.FormatStillPressedNote(2, "Editor");
            Assert.That(
                note,
                Is.EqualTo(
                    "2 key(s) still report pressed in the Editor view; the release may not yet be visible to gameplay polling."));
        }

        /// <summary>
        /// Verifies ReleaseAll appends the still-pressed note only when at least one key still reads pressed.
        /// </summary>
        [Test]
        public void AppendStillPressedNote_WhenOneKeyStillPressed_AppendsExactLiteral()
        {
            ReleasedKeyState[] states =
            {
                new ReleasedKeyState { Key = "W", DeviceIsPressedAfterRelease = false },
                new ReleasedKeyState { Key = "D", DeviceIsPressedAfterRelease = true }
            };

            string message = SimulateKeyboardReleaseMessageFormatter.AppendStillPressedNote(
                "Released 2 key(s): D, W",
                states,
                "Editor");

            Assert.That(
                message,
                Is.EqualTo(
                    "Released 2 key(s): D, W 1 key(s) still report pressed in the Editor view; the release may not yet be visible to gameplay polling."));
        }

        /// <summary>
        /// Verifies a clean ReleaseAll message is left unchanged when every readback is unpressed.
        /// </summary>
        [Test]
        public void AppendStillPressedNote_WhenNoKeyStillPressed_ReturnsMessageUnchanged()
        {
            ReleasedKeyState[] states =
            {
                new ReleasedKeyState { Key = "W", DeviceIsPressedAfterRelease = false },
                new ReleasedKeyState { Key = "D", DeviceIsPressedAfterRelease = false }
            };

            string message = SimulateKeyboardReleaseMessageFormatter.AppendStillPressedNote(
                "Released 2 key(s): D, W",
                states,
                "Editor");

            Assert.That(message, Is.EqualTo("Released 2 key(s): D, W"));
        }

        /// <summary>
        /// Verifies the deferred latch-sync note matches the exact English sentence callers will see.
        /// </summary>
        [Test]
        public void AppendDeferredLatchSyncNote_WhenScheduled_AppendsExactLiteral()
        {
            string message = SimulateKeyboardReleaseMessageFormatter.AppendDeferredLatchSyncNote(
                "Released 1 key(s): D",
                true);

            Assert.That(
                message,
                Is.EqualTo(
                    "Released 1 key(s): D A deferred latch sync will run on the next player input update."));
        }

        /// <summary>
        /// Verifies the deferred latch-sync note is omitted when nothing was scheduled.
        /// </summary>
        [Test]
        public void AppendDeferredLatchSyncNote_WhenNotScheduled_ReturnsMessageUnchanged()
        {
            string message = SimulateKeyboardReleaseMessageFormatter.AppendDeferredLatchSyncNote(
                "Released all keys (none were held).",
                false);

            Assert.That(message, Is.EqualTo("Released all keys (none were held)."));
        }
    }

    /// <summary>
    /// Verifies ReleaseAll device-state mapping copies the injected isPressed reader onto the DTO.
    /// </summary>
    public sealed class ReleasedKeyStateMappingTests
    {
        /// <summary>
        /// Verifies a fake reader that reports one key pressed is copied as true onto ReleasedKeyState,
        /// so a hardcoded-false mapper cannot pass.
        /// </summary>
        [Test]
        public void MapReleasedKeyStates_WhenFakeReaderReportsOneKeyPressed_CopiesTrueOntoDto()
        {
            string[] releasedNames = { "W", "D" };
            Key[] sortedKeys = { Key.W, Key.D };

            List<ReleasedKeyState> states = KeyboardInputMainThreadCleanup.MapReleasedKeyStates(
                releasedNames,
                sortedKeys,
                key => key == Key.D);

            Assert.That(states, Has.Count.EqualTo(2));
            Assert.That(states[0].Key, Is.EqualTo("W"));
            Assert.That(states[0].DeviceIsPressedAfterRelease, Is.False);
            Assert.That(states[1].Key, Is.EqualTo("D"));
            Assert.That(states[1].DeviceIsPressedAfterRelease, Is.True);
        }
    }
}
#endif
