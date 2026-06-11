#if ULOOP_HAS_INPUT_SYSTEM
#nullable enable
using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using UnityEngine.TestTools;
using Object = UnityEngine.Object;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Test fixture that verifies Simulate Keyboard behavior.
    /// </summary>
    public class SimulateKeyboardTests : InputTestFixture
    {
        private GameObject eventSystemGo = null!;
        private GameObject framePressObserverGo = null!;
        private ExistingEventSystemDisableScope eventSystemDisableScope = null!;
        private TestableSimulateKeyboardTool tool = null!;
        private SimulateKeyboardResponse lastResponse = null!;
        private Keyboard keyboard = null!;
        private FramePressObserver framePressObserver = null!;
        private UpdateFramePressObserver updateFramePressObserver = null!;
        private FrameStateObserver frameStateObserver = null!;
        private WasPressedGameplayJumpController gameplayJumpController = null!;
        private ManualModeFramePressObserver manualModeFramePressObserver = null!;
        private InputSettings.UpdateMode originalUpdateMode;
        private float originalTimeScale;

        public override void Setup()
        {
            base.Setup();
            InputSettings settings = RequireInputSettings();
            originalUpdateMode = settings.updateMode;
            originalTimeScale = Time.timeScale;

            eventSystemDisableScope = new ExistingEventSystemDisableScope();
            eventSystemGo = new GameObject("TestEventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            framePressObserverGo = new GameObject("FramePressObserver");
            framePressObserver = framePressObserverGo.AddComponent<FramePressObserver>();
            updateFramePressObserver = framePressObserverGo.AddComponent<UpdateFramePressObserver>();
            frameStateObserver = framePressObserverGo.AddComponent<FrameStateObserver>();
            gameplayJumpController = framePressObserverGo.AddComponent<WasPressedGameplayJumpController>();
            manualModeFramePressObserver = framePressObserverGo.AddComponent<ManualModeFramePressObserver>();

            tool = new TestableSimulateKeyboardTool();
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        public override void TearDown()
        {
            InputSystemUpdateHelper.ResetPauseProviderForTests();
            InputSystemUpdateHelper.ResetTimeoutsForTests();
            UloopPausePointRegistry.ResetForTests();
            InputSettings settings = RequireInputSettings();
            settings.updateMode = originalUpdateMode;
            Time.timeScale = originalTimeScale;
            KeyboardKeyState.ReleaseAllKeys();
            SimulateKeyboardOverlayState.Clear();
            InputVisualizationCanvas[] canvases =
                Object.FindObjectsByType<InputVisualizationCanvas>(FindObjectsSortMode.None);
            for (int i = 0; i < canvases.Length; i++)
            {
                Object.DestroyImmediate(canvases[i].gameObject);
            }
            Object.DestroyImmediate(framePressObserverGo);
            Object.DestroyImmediate(eventSystemGo);
            eventSystemDisableScope.Restore();
            base.TearDown();
        }

        #region Press Tests

        [UnityTest]
        public IEnumerator Press_Should_InjectKeyDownAndUp()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "W"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("Press", lastResponse.Action);
            Assert.AreEqual("W", lastResponse.KeyName);
            // After press completes, key should be released
            Assert.IsFalse(keyboard[Key.W].isPressed, "Key should be released after press");
        }

        [UnityTest]
        public IEnumerator Press_Should_ReportObservedPressEdge()
        {
            // Verifies the response tells callers whether wasPressedThisFrame was actually
            // observable, so agents can distinguish a delivered edge from a missed one.
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(
                lastResponse.PressEdgeObserved.HasValue,
                "Press must report press-edge observability");
            Assert.IsTrue(
                lastResponse.PressEdgeObserved!.Value,
                "A successful Press in PlayMode should observe the press edge");
        }

        [UnityTest]
        public IEnumerator KeyDown_Should_ReportObservedPressEdge()
        {
            // Verifies KeyDown also reports edge observability, because agents fall back to it
            // when Press appears to be missed by gameplay polling.
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "Space"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(
                lastResponse.PressEdgeObserved.HasValue && lastResponse.PressEdgeObserved.Value,
                "A successful KeyDown in PlayMode should observe the press edge");

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyUp.ToString(),
                ["key"] = "Space"
            });
        }

        [UnityTest]
        public IEnumerator Press_WithDuration_Should_HoldKey()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space",
                ["duration"] = 0.1f
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("Space", lastResponse.KeyName);
        }

        [UnityTest]
        public IEnumerator Press_WhenUnityPausesDuringObservation_Should_CompleteAsPausePointInterruption()
        {
            // Verifies that a pause-point pause releases the tool slot instead of leaving the press command busy.
            yield return null;

            SimulateKeyboardSchema parameters = new()
            {
                Action = UnityCliLoopKeyboardAction.Press,
                Key = "Space",
                Duration = 1f
            };
            Task<SimulateKeyboardResponse> task =
                tool.ExecuteWithCancellationAsync(parameters, CancellationToken.None);

            yield return new WaitUntil(() => keyboard[Key.Space].isPressed || task.IsCompleted);
            Assert.IsFalse(task.IsCompleted, "The test must pause during the press observation window.");

            InputSystemUpdateHelper.ConfigurePauseProviderForTests(() => true);
            yield return WaitForTask(task);
            InputSystemUpdateHelper.ResetPauseProviderForTests();

            lastResponse = task.Result;
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(lastResponse.InterruptedByPausePoint);
            Assert.AreEqual("Press", lastResponse.Action);
            Assert.AreEqual("Space", lastResponse.KeyName);
            Assert.IsNull(lastResponse.PausePointId);
            Assert.IsNull(lastResponse.PausePointHitCount);
            Assert.IsTrue(
                lastResponse.PressEdgeObserved.HasValue,
                "Interrupted presses must still report whether the press edge was observed.");
            Assert.IsTrue(
                lastResponse.PressEdgeObserved!.Value,
                "The press reached isPressed through gameplay updates, so the edge must have been observed.");
            Assert.IsFalse(keyboard[Key.Space].isPressed, "Pause-point interruption should release the injected key state.");
            Assert.IsFalse(SimulateKeyboardOverlayState.IsActive, "Pause-point interruption should clear keyboard overlay state.");
        }

        [UnityTest]
        public IEnumerator Press_WhenPausePointMarkerHits_Should_ReturnMarkerDetails()
        {
            // Verifies marker-caused interruption reports the marker id and hit count.
            yield return null;

            UloopPausePointRegistry.ConfigureForTests(
                new FakePausePointPauseController(),
                () => new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc));
            UloopPausePointRegistry.Enable("space-press", 30);
            SimulateKeyboardSchema parameters = new()
            {
                Action = UnityCliLoopKeyboardAction.Press,
                Key = "Space",
                Duration = 1f
            };
            Task<SimulateKeyboardResponse> task =
                tool.ExecuteWithCancellationAsync(parameters, CancellationToken.None);

            yield return new WaitUntil(() => keyboard[Key.Space].isPressed || task.IsCompleted);
            Assert.IsFalse(task.IsCompleted, "The test must pause during the press observation window.");

            UloopPausePoint.Pause("space-press");
            InputSystemUpdateHelper.ConfigurePauseProviderForTests(() => true);
            yield return WaitForTask(task);
            InputSystemUpdateHelper.ResetPauseProviderForTests();

            lastResponse = task.Result;
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(lastResponse.InterruptedByPausePoint);
            Assert.AreEqual("space-press", lastResponse.PausePointId);
            Assert.AreEqual(1, lastResponse.PausePointHitCount);
            Assert.IsTrue(
                lastResponse.PressEdgeObserved.HasValue,
                "Marker-interrupted presses must still report whether the press edge was observed.");
            Assert.IsFalse(keyboard[Key.Space].isPressed, "Marker interruption should release the injected key state.");
        }

        [UnityTest]
        public IEnumerator Press_WhenMultiplePausePointMarkersHit_Should_ListAllHits()
        {
            // Verifies the response lists every marker hit during the press, not just the latest.
            yield return null;

            UloopPausePointRegistry.ConfigureForTests(
                new FakePausePointPauseController(),
                () => new DateTime(2026, 6, 3, 0, 0, 0, DateTimeKind.Utc));
            UloopPausePointRegistry.Enable("space-press", 30);
            UloopPausePointRegistry.Enable("space-press-followup", 30);
            SimulateKeyboardSchema parameters = new()
            {
                Action = UnityCliLoopKeyboardAction.Press,
                Key = "Space",
                Duration = 1f
            };
            Task<SimulateKeyboardResponse> task =
                tool.ExecuteWithCancellationAsync(parameters, CancellationToken.None);

            yield return new WaitUntil(() => keyboard[Key.Space].isPressed || task.IsCompleted);
            Assert.IsFalse(task.IsCompleted, "The test must pause during the press observation window.");

            UloopPausePoint.Pause("space-press");
            UloopPausePoint.Pause("space-press-followup");
            InputSystemUpdateHelper.ConfigurePauseProviderForTests(() => true);
            yield return WaitForTask(task);
            InputSystemUpdateHelper.ResetPauseProviderForTests();

            lastResponse = task.Result;
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(lastResponse.InterruptedByPausePoint);
            Assert.IsNotNull(lastResponse.PausePointHits, "All hit markers must be listed.");
            Assert.AreEqual(2, lastResponse.PausePointHits!.Count);
            Assert.AreEqual("space-press", lastResponse.PausePointHits[0].Id);
            Assert.AreEqual("space-press-followup", lastResponse.PausePointHits[1].Id);
        }

        [UnityTest]
        public IEnumerator Press_Cancellation_Should_ClearPressOverlay()
        {
            // Verifies that canceling an applied press releases input and clears transient overlay state.
            yield return null;

            SimulateKeyboardSchema parameters = new()
            {
                Action = UnityCliLoopKeyboardAction.Press,
                Key = "Space",
                Duration = 2f
            };
            CancellationTokenSource cts = new();
            Task<SimulateKeyboardResponse> task = tool.ExecuteWithCancellationAsync(parameters, cts.Token);

            yield return new WaitUntil(() => keyboard[Key.Space].isPressed || task.IsCompleted);

            Assert.IsFalse(task.IsCompleted, "Cancellation test must interrupt the applied press.");
            Assert.AreEqual("Space", SimulateKeyboardOverlayState.PressKey, "Applied press should show transient overlay state before cancellation.");

            cts.Cancel();
            yield return WaitForTask(task, allowCanceled: true);

            Assert.IsTrue(task.IsCanceled, "Press cancellation should remain visible to the caller.");
            Assert.IsFalse(keyboard[Key.Space].isPressed, "Canceled Press should release the injected key state.");
            Assert.IsNull(SimulateKeyboardOverlayState.PressKey, "Canceled Press should clear transient overlay state.");
        }

        [UnityTest]
        public IEnumerator Press_Space_Should_SetWasPressedThisFrame()
        {
            yield return null;

            framePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space",
                ["duration"] = 0.1f
            });

            Assert.Greater(framePressObserver.SpacePressedFrameCount, 0, "Space press should be visible via wasPressedThisFrame");
        }

        [UnityTest]
        public IEnumerator Press_WithoutDuration_Should_BehaveAsTap()
        {
            yield return null;

            framePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space"
            });

            Assert.Greater(framePressObserver.SpacePressedFrameCount, 0, "Zero-duration press should still be visible as a tap");
            Assert.IsFalse(keyboard[Key.Space].isPressed, "Zero-duration press should release the key after the tap");
        }

        [UnityTest]
        public IEnumerator Press_WithoutDuration_Should_BeVisibleToGameplayUpdate()
        {
            // Verifies that default Press stays alive long enough for Update polling to observe wasPressedThisFrame.
            yield return null;

            updateFramePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space"
            });

            Assert.Greater(updateFramePressObserver.SpacePressedUpdateCount, 0, "Default Press should be visible to MonoBehaviour.Update via wasPressedThisFrame");
        }

        [UnityTest]
        public IEnumerator Press_WithoutDuration_Should_StayHeldForGameplayObservationFrames()
        {
            // Verifies that default Press is not released before gameplay Update can observe the held state.
            yield return null;

            updateFramePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space"
            });

            Assert.GreaterOrEqual(updateFramePressObserver.SpaceHeldUpdateCount, 2, "Default Press should stay held across gameplay observation frames");
        }

        [UnityTest]
        public IEnumerator Press_WithoutDuration_Should_TriggerGameplayJumpFromWasPressedThisFrame()
        {
            // Verifies that default Press drives gameplay state transitions that poll wasPressedThisFrame in Update.
            yield return null;

            gameplayJumpController.ResetState();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space"
            });

            Assert.AreEqual(1, gameplayJumpController.JumpCount, "Default Press should trigger a single gameplay jump.");
            Assert.IsFalse(gameplayJumpController.Grounded, "Gameplay state should become airborne after the jump.");
            Assert.Greater(gameplayJumpController.VerticalPosition, 0f, "Gameplay jump should move the controller upward.");
        }

        [UnityTest]
        public IEnumerator Press_InFixedMode_Should_BeVisibleToGameplayUpdate()
        {
            // Verifies that Press still reaches Update polling when the project processes input in FixedUpdate.
            yield return null;

            InputSettings settings = RequireInputSettings();
            settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            updateFramePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space",
                ["duration"] = 0.2f
            });

            Assert.Greater(updateFramePressObserver.SpacePressedUpdateCount, 0, "Fixed-update input processing should still be visible to MonoBehaviour.Update via wasPressedThisFrame");
        }

        [UnityTest]
        public IEnumerator Press_Should_KeepHeldOverlayKeys()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "LeftShift"
            });
            Assert.IsTrue(lastResponse.Success);

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space"
            });

            CollectionAssert.Contains(SimulateKeyboardOverlayState.HeldKeys, "LeftShift", "Press should not clear held-key overlay badges");
        }

        [UnityTest]
        public IEnumerator Press_Should_KeepTransientBadgeVisibleAfterCompletion()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.AreEqual("Space", SimulateKeyboardOverlayState.PressKey, "Completed presses should remain visible for screenshot verification.");
            Assert.IsFalse(SimulateKeyboardOverlayState.IsPressHeld, "Completed presses should move to the released-display state.");

            yield return null;

            BadgeVisual badge = RequireBadgeVisual("Space");
            Assert.AreEqual(SimulateKeyboardOverlay.CONTAINER_BACKGROUND_ALPHA, badge.BackgroundAlpha, 0.01f, "Released press badge should still be fully visible right after the tool returns.");
            Assert.AreEqual(1f, badge.TextAlpha, 0.01f, "Released press text should still be fully visible right after the tool returns.");
        }

        [UnityTest]
        public IEnumerator Press_Should_CreateOverlayCanvas_WithUnitScale()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Space"
            });

            InputVisualizationCanvas canvas = Object.FindAnyObjectByType<InputVisualizationCanvas>();
            Assert.IsNotNull(canvas, "InputVisualizationCanvas must exist after keyboard simulation.");
            Assert.AreEqual(Vector3.one, canvas.transform.localScale, "Overlay canvas should use unit scale so GameView resolution changes do not collapse the UI.");
        }

        [UnityTest]
        public IEnumerator Press_Should_RestoreRunInBackground_WhenOriginallyDisabled()
        {
            // Verifies that keyboard simulation keeps PlayMode running in the background only during execution.
            yield return null;

            bool originalRunInBackground = UnityEngine.Application.runInBackground;

            try
            {
                UnityEngine.Application.runInBackground = false;
                Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(new JObject
                {
                    ["action"] = KeyboardAction.Press.ToString(),
                    ["key"] = "Space",
                    ["duration"] = 0.2f
                }, CancellationToken.None);

                float toggleTimeoutAt = Time.realtimeSinceStartup + 2f;
                yield return new WaitUntil(() =>
                    UnityEngine.Application.runInBackground
                    || task.IsCompleted
                    || Time.realtimeSinceStartup >= toggleTimeoutAt);
                Assert.IsTrue(
                    UnityEngine.Application.runInBackground,
                    "Keyboard simulation should enable Run In Background while executing.");

                float completionTimeoutAt = Time.realtimeSinceStartup + 5f;
                yield return new WaitUntil(() =>
                    task.IsCompleted || Time.realtimeSinceStartup >= completionTimeoutAt);
                Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
                Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");

                lastResponse = (SimulateKeyboardResponse)task.Result;
                Assert.IsTrue(lastResponse.Success);
                Assert.IsFalse(
                    UnityEngine.Application.runInBackground,
                    "Keyboard simulation should restore the original Run In Background value.");
            }
            finally
            {
                UnityEngine.Application.runInBackground = originalRunInBackground;
            }
        }

        [UnityTest]
        public IEnumerator OverlayFade_Should_StartAfterPressRelease_And_NotDimHeldKeyBadge()
        {
            yield return null;

            SimulateKeyboardOverlayState.AddHeldKey("LeftShift");
            SimulateKeyboardOverlayState.ShowPress("Space");

            GameObject? canvasPrefab = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(
                "Packages/io.github.hatayama.uloopmcp/Runtime/Common/InputVisualizationCanvas.prefab");
            Debug.Assert(canvasPrefab != null, "InputVisualizationCanvas prefab must exist");
            Object.Instantiate(canvasPrefab!);

            yield return null;
            yield return new WaitForSecondsRealtime(0.55f);
            yield return null;

            BadgeVisual heldBadgeWhilePressHeld = RequireBadgeVisual("LeftShift");
            BadgeVisual pressBadgeWhileHeld = RequireBadgeVisual("Space");

            Assert.AreEqual(SimulateKeyboardOverlay.CONTAINER_BACKGROUND_ALPHA, heldBadgeWhilePressHeld.BackgroundAlpha, 0.01f, "Held-key badge should keep full opacity while another key is held.");
            Assert.AreEqual(1f, heldBadgeWhilePressHeld.TextAlpha, 0.01f, "Held-key text should keep full opacity while another key is held.");
            Assert.AreEqual(SimulateKeyboardOverlay.CONTAINER_BACKGROUND_ALPHA, pressBadgeWhileHeld.BackgroundAlpha, 0.01f, "Long presses should stay visible until the key is released.");
            Assert.AreEqual(1f, pressBadgeWhileHeld.TextAlpha, 0.01f, "Long-press text should stay visible until the key is released.");

            SimulateKeyboardOverlayState.ReleasePress();
            yield return null;
            yield return new WaitForSecondsRealtime(0.55f);
            yield return null;

            BadgeVisual heldBadgeAfterRelease = RequireBadgeVisual("LeftShift");
            BadgeVisual pressBadgeAfterRelease = RequireBadgeVisual("Space");

            Assert.AreEqual(SimulateKeyboardOverlay.CONTAINER_BACKGROUND_ALPHA, heldBadgeAfterRelease.BackgroundAlpha, 0.01f, "Container background should stay full while a held key exists.");
            Assert.AreEqual(1f, heldBadgeAfterRelease.TextAlpha, 0.01f, "Held-key text should remain fully visible while transient presses fade.");
            // Container background stays full because held key keeps it opaque;
            // only the press key's text alpha fades.
            Assert.AreEqual(SimulateKeyboardOverlay.CONTAINER_BACKGROUND_ALPHA, pressBadgeAfterRelease.BackgroundAlpha, 0.01f, "Container background should stay full while a held key exists.");
            Assert.Less(pressBadgeAfterRelease.TextAlpha, 1f, "Transient press text should fade only after release.");
        }

        [UnityTest]
        public IEnumerator Press_Enter_Should_SetWasPressedThisFrame()
        {
            yield return null;

            framePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Enter",
                ["duration"] = 0.1f
            });

            Assert.Greater(framePressObserver.EnterPressedFrameCount, 0, "Enter press should be visible via wasPressedThisFrame");
        }

        [UnityTest]
        public IEnumerator Press_InManualMode_Should_NotHang()
        {
            yield return null;

            InputSettings settings = RequireInputSettings();
            settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
            framePressObserver.ResetCount();
            manualModeFramePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Enter"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.Greater(framePressObserver.EnterPressedFrameCount, 0, "Manual-mode press should advance input and register the tap");
            Assert.Greater(manualModeFramePressObserver.EnterPressedStateCount, 0, "Manual-mode zero-duration press should remain visible to the project's own manual update loop.");
            Assert.IsFalse(keyboard[Key.Enter].isPressed, "Manual-mode press should release the key after the tap");
        }

        [UnityTest]
        public IEnumerator Press_InPausedFixedMode_Should_NotHang()
        {
            yield return null;

            InputSettings settings = RequireInputSettings();
            settings.updateMode = InputSettings.UpdateMode.ProcessEventsInFixedUpdate;
            Time.timeScale = 0f;
            framePressObserver.ResetCount();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "Enter"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.Greater(framePressObserver.EnterPressedFrameCount, 0, "Paused fixed-update presses should follow the resolved dynamic update");
            Assert.IsFalse(keyboard[Key.Enter].isPressed, "Paused fixed-update press should release the key after the tap");
        }

        [UnityTest]
        public IEnumerator Press_WithInvalidKey_Should_ReturnFailure()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = "InvalidKeyName"
            });

            Assert.IsFalse(lastResponse.Success);
            StringAssert.Contains("Invalid key name", lastResponse.Message);
        }

        [UnityTest]
        public IEnumerator Press_WithEmptyKey_Should_ReturnFailure()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.Press.ToString(),
                ["key"] = ""
            });

            Assert.IsFalse(lastResponse.Success);
            StringAssert.Contains("Key parameter is required", lastResponse.Message);
        }

        #endregion

        #region KeyDown / KeyUp Tests

        [UnityTest]
        public IEnumerator KeyDown_Should_HoldKeyUntilKeyUp()
        {
            yield return null;

            frameStateObserver.ResetCounts();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(KeyboardKeyState.IsKeyHeld(Key.W), "Key should be held after KeyDown");
            Assert.Greater(frameStateObserver.WPressedUpdateCount, 0, "KeyDown should wait until Update observed the pressed key");

            frameStateObserver.ResetCounts();
            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyUp.ToString(),
                ["key"] = "W"
            });

            Assert.IsTrue(lastResponse.Success);
            Assert.IsFalse(KeyboardKeyState.IsKeyHeld(Key.W), "Key should be released after KeyUp");
            Assert.Greater(frameStateObserver.WReleasedUpdateCount, 0, "KeyUp should wait until Update observed the released key");
        }

        [UnityTest]
        public IEnumerator KeyDown_Should_TriggerGameplayJumpFromWasPressedThisFrame()
        {
            // Verifies that KeyDown exposes its initial edge to gameplay before returning as a held key.
            yield return null;

            gameplayJumpController.ResetState();

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "Space"
            });

            Assert.AreEqual(1, gameplayJumpController.JumpCount, "KeyDown should trigger a single gameplay jump on the initial edge.");
            Assert.IsFalse(gameplayJumpController.Grounded, "Gameplay state should become airborne after KeyDown.");
            Assert.IsTrue(keyboard[Key.Space].isPressed, "KeyDown should remain held after gameplay observes the edge.");

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyUp.ToString(),
                ["key"] = "Space"
            });
            Assert.IsTrue(lastResponse.Success);
        }

        [UnityTest]
        public IEnumerator KeyDown_WhenAlreadyHeld_Should_ReturnFailure()
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
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });
            Assert.IsFalse(lastResponse.Success);
            StringAssert.Contains("already held", lastResponse.Message);
        }

        [UnityTest]
        public IEnumerator KeyUp_WhenNotHeld_Should_ReturnFailure()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyUp.ToString(),
                ["key"] = "W"
            });

            Assert.IsFalse(lastResponse.Success);
            StringAssert.Contains("not currently held", lastResponse.Message);
        }

        [UnityTest]
        public IEnumerator MultipleKeys_Should_SupportSimultaneousHold()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "LeftShift"
            });
            Assert.IsTrue(lastResponse.Success);

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });
            Assert.IsTrue(lastResponse.Success);

            Assert.IsTrue(KeyboardKeyState.IsKeyHeld(Key.LeftShift), "LeftShift should be held");
            Assert.IsTrue(KeyboardKeyState.IsKeyHeld(Key.W), "W should be held");

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyUp.ToString(),
                ["key"] = "W"
            });
            Assert.IsTrue(lastResponse.Success);
            Assert.IsTrue(KeyboardKeyState.IsKeyHeld(Key.LeftShift), "LeftShift should still be held");
            Assert.IsFalse(KeyboardKeyState.IsKeyHeld(Key.W), "W should be released");

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyUp.ToString(),
                ["key"] = "LeftShift"
            });
            Assert.IsTrue(lastResponse.Success);
        }

        #endregion

        #region State Management Tests

        [UnityTest]
        public IEnumerator ReleaseAllKeys_Should_ClearAllHeldKeys()
        {
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "LeftShift"
            });

            Assert.IsTrue(KeyboardKeyState.IsKeyHeld(Key.W));
            Assert.IsTrue(KeyboardKeyState.IsKeyHeld(Key.LeftShift));

            KeyboardKeyState.ReleaseAllKeys();

            Assert.IsFalse(KeyboardKeyState.IsKeyHeld(Key.W));
            Assert.IsFalse(KeyboardKeyState.IsKeyHeld(Key.LeftShift));
            Assert.AreEqual(0, KeyboardKeyState.HeldKeys.Count);
        }

        [UnityTest]
        public IEnumerator ReleaseAllKeys_Should_ClearTransientPressKeys()
        {
            yield return null;

            KeyboardKeyState.RegisterTransientKey(Key.Space);
            KeyboardKeyState.ReleaseAllKeys();

            Assert.IsFalse(keyboard[Key.Space].isPressed, "ReleaseAllKeys should release transient press keys");
        }

        [UnityTest]
        public IEnumerator ReleaseAllKeys_WithoutKeyboard_Should_ClearTransientKeys()
        {
            yield return null;

            KeyboardKeyState.RegisterTransientKey(Key.Space);
            InputSystem.RemoveDevice(keyboard);

            KeyboardKeyState.ReleaseAllKeys();

            Keyboard recreatedKeyboard = InputSystem.AddDevice<Keyboard>();
            KeyboardKeyState.SetKeyState(recreatedKeyboard, Key.W, true);

            Assert.IsFalse(recreatedKeyboard[Key.Space].isPressed, "Cleanup without a keyboard should not leak transient keys into later events.");
            Assert.IsTrue(recreatedKeyboard[Key.W].isPressed, "Later simulated events should still apply to the intended key.");
        }

        [UnityTest]
        public IEnumerator KeyDown_Cancellation_Should_RollBackHeldState()
        {
            yield return null;

            SimulateKeyboardSchema parameters = new()
            {
                Action = UnityCliLoopKeyboardAction.KeyDown,
                Key = "W"
            };
            CancellationTokenSource cts = new();
            Task<SimulateKeyboardResponse> task = tool.ExecuteWithCancellationAsync(parameters, cts.Token);

            yield return new WaitUntil(() => KeyboardKeyState.IsKeyHeld(Key.W) || task.IsCompleted);

            Assert.IsTrue(KeyboardKeyState.IsKeyHeld(Key.W), "Cancellation test must wait until key-down state is applied.");
            Assert.IsFalse(task.IsCompleted, "Cancellation test must interrupt the frame-wait phase, not a completed key-down.");

            cts.Cancel();
            yield return WaitForTask(task, allowCanceled: true);

            Assert.IsTrue(task.IsCanceled, "KeyDown should preserve cancellation outward after cleanup.");
            Assert.IsFalse(KeyboardKeyState.IsKeyHeld(Key.W), "Canceled KeyDown should roll back held-key bookkeeping.");
            CollectionAssert.DoesNotContain(SimulateKeyboardOverlayState.HeldKeys, "W", "Canceled KeyDown should clear the overlay badge.");

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });

            Assert.IsTrue(lastResponse.Success, "Canceled KeyDown cleanup should leave later key-down requests usable.");
        }

        [UnityTest]
        public IEnumerator KeyUp_CancellationAfterRelease_Should_ClearHeldState()
        {
            // Verifies that canceling KeyUp after release does not leave held-key bookkeeping behind.
            yield return null;

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });
            Assert.IsTrue(lastResponse.Success, "KeyUp cancellation test requires an initially held key.");
            Assert.IsTrue(KeyboardKeyState.IsKeyHeld(Key.W), "KeyUp cancellation test requires held-key bookkeeping.");

            SimulateKeyboardSchema parameters = new()
            {
                Action = UnityCliLoopKeyboardAction.KeyUp,
                Key = "W"
            };
            CancellationTokenSource cts = new();
            Task<SimulateKeyboardResponse> task = tool.ExecuteWithCancellationAsync(parameters, cts.Token);
            cts.Cancel();

            yield return WaitForTask(task, allowCanceled: true);

            Assert.IsTrue(task.IsCanceled, "KeyUp cancellation should remain visible to the caller.");
            Assert.IsFalse(keyboard[Key.W].isPressed, "Canceled KeyUp should release the physical key state.");
            Assert.IsFalse(KeyboardKeyState.IsKeyHeld(Key.W), "Canceled KeyUp should clear held-key bookkeeping.");
            CollectionAssert.DoesNotContain(SimulateKeyboardOverlayState.HeldKeys, "W", "Canceled KeyUp should clear the overlay badge.");

            yield return RunTool(new JObject
            {
                ["action"] = KeyboardAction.KeyDown.ToString(),
                ["key"] = "W"
            });

            Assert.IsTrue(lastResponse.Success, "Canceled KeyUp cleanup should leave later key-down requests usable.");
        }

        [UnityTest]
        public IEnumerator ApplyOnNextConfiguredUpdate_CancellationBeforeInputUpdate_ShouldRemoveCallback()
        {
            // Verifies that cancellation removes the pending Input System callback before the next update.
            yield return null;

            InputSettings settings = RequireInputSettings();
            settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            Time.timeScale = 1f;
            int applyCount = 0;
            CancellationTokenSource cts = new();

            Task<InputSimulationWaitOutcome> task = InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => applyCount++,
                cts.Token);

            Assert.AreEqual(1, InputSystemUpdateHelper.PendingConfiguredUpdateCallbackCount);

            cts.Cancel();
            yield return WaitForTask(task, allowCanceled: true);

            Assert.IsTrue(task.IsCanceled, "Apply cancellation should remain visible to the caller.");
            Assert.AreEqual(0, applyCount, "Canceled apply should not run after callback cleanup.");
            Assert.AreEqual(0, InputSystemUpdateHelper.PendingConfiguredUpdateCallbackCount, "Canceled apply should remove the pending Input System callback.");
        }

        [UnityTest]
        public IEnumerator ApplyOnNextConfiguredUpdate_WhenApplyThrows_ShouldFaultAndRemoveCallback()
        {
            // Verifies that apply failures complete the wait as faulted instead of leaving the callback pending.
            yield return null;

            InputSettings settings = RequireInputSettings();
            settings.updateMode = InputSettings.UpdateMode.ProcessEventsInDynamicUpdate;
            InvalidOperationException expectedException = new("Apply failed.");
            Task<InputSimulationWaitOutcome> task = InputSystemUpdateHelper.ApplyOnNextConfiguredUpdate(
                () => throw expectedException,
                CancellationToken.None);

            Assert.AreEqual(1, InputSystemUpdateHelper.PendingConfiguredUpdateCallbackCount);

            yield return WaitForTask(task, allowFaulted: true);

            Assert.IsTrue(task.IsFaulted, "Apply failures should fault the returned task.");
            Assert.AreSame(expectedException, task.Exception?.GetBaseException());
            Assert.AreEqual(0, InputSystemUpdateHelper.PendingConfiguredUpdateCallbackCount, "Faulted apply should remove the pending Input System callback.");
        }

        [UnityTest]
        public IEnumerator WaitForRuntimeFrames_WhenFrameGoalCannotComplete_ShouldReturnTimedOut()
        {
            // Verifies that frame observation has a wall-clock guard and does not wait forever.
            yield return null;

            InputSystemUpdateHelper.ConfigureTimeoutsForTests(50, 50);
            Task<InputSimulationWaitOutcome> task =
                InputSystemUpdateHelper.WaitForRuntimeFrames(int.MaxValue, CancellationToken.None);

            yield return WaitForTask(task);

            Assert.AreEqual(InputSimulationWaitOutcome.TimedOut, task.Result);
            Assert.AreEqual(0, EditorFrameWaiter.PendingWaitCount, "Timed-out frame observation should cancel its pending frame wait.");
        }

        #endregion

        #region Helpers

        private IEnumerator RunTool(JObject parameters)
        {
            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(parameters, System.Threading.CancellationToken.None);
            yield return WaitForTask(task);
            lastResponse = (SimulateKeyboardResponse)task.Result;
        }

        private static IEnumerator WaitForTask(Task task, bool allowCanceled = false, bool allowFaulted = false)
        {
            float timeoutAt = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() =>
                task.IsCompleted || Time.realtimeSinceStartup >= timeoutAt);
            Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
            if (!allowCanceled)
            {
                Assert.IsFalse(task.IsCanceled, "Tool execution should not be canceled.");
            }
            if (!allowFaulted)
            {
                Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");
            }
        }

        private static InputSettings RequireInputSettings()
        {
            InputSettings? settings = InputSystem.settings;
            Debug.Assert(settings != null, "InputSystem.settings must be available in SimulateKeyboardTests");
            return settings!;
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

        private static BadgeVisual RequireBadgeVisual(string keyName)
        {
            InputVisualizationCanvas canvas = Object.FindAnyObjectByType<InputVisualizationCanvas>();
            Debug.Assert(canvas != null, "InputVisualizationCanvas must exist");
            SimulateKeyboardOverlay overlay = canvas!.KeyboardOverlay;
            Assert.IsNotNull(overlay, "KeyboardOverlay must exist before reading badge visuals.");

            // Container Image holds the shared background alpha for all badges
            Image? containerImage = overlay!.GetComponentInChildren<Image>(true);
            Assert.IsNotNull(containerImage, "Container background image should exist.");
            float containerAlpha = containerImage!.color.a;

            string symbol = KeySymbolMap.GetSymbol(keyName);
            Text[] texts = overlay.gameObject.GetComponentsInChildren<Text>(true);
            for (int i = 0; i < texts.Length; i++)
            {
                if (texts[i].text != symbol)
                {
                    continue;
                }

                return new BadgeVisual(containerAlpha, texts[i].color.a);
            }

            Assert.Fail($"Badge '{keyName}' (symbol: '{symbol}') was not found.");
            return default;
        }

        #endregion

        private readonly struct BadgeVisual
        {
            public readonly float BackgroundAlpha;
            public readonly float TextAlpha;

            public BadgeVisual(float backgroundAlpha, float textAlpha)
            {
                BackgroundAlpha = backgroundAlpha;
                TextAlpha = textAlpha;
            }
        }
    }

    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class FramePressObserver : MonoBehaviour
    {
        public int SpacePressedFrameCount { get; private set; }
        public int EnterPressedFrameCount { get; private set; }

        private void OnEnable()
        {
            InputSystem.onAfterUpdate += HandleAfterUpdate;
        }

        private void OnDisable()
        {
            InputSystem.onAfterUpdate -= HandleAfterUpdate;
        }

        // The tool follows the configured Input System update mode, so the
        // observer must sample wasPressedThisFrame from the same update loop.
        private void HandleAfterUpdate()
        {
            InputUpdateType expectedUpdateType = InputUpdateTypeResolver.Resolve();
            InputUpdateType currentUpdateType = InputState.currentUpdateType;
            if (!InputUpdateTypeResolver.IsMatch(currentUpdateType, expectedUpdateType))
            {
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                SpacePressedFrameCount++;
            }

            if (keyboard.enterKey.wasPressedThisFrame)
            {
                EnterPressedFrameCount++;
            }
        }

        public void ResetCount()
        {
            SpacePressedFrameCount = 0;
            EnterPressedFrameCount = 0;
        }
    }

    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class UpdateFramePressObserver : MonoBehaviour
    {
        public int SpacePressedUpdateCount { get; private set; }
        public int SpaceHeldUpdateCount { get; private set; }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                SpacePressedUpdateCount++;
            }

            if (keyboard.spaceKey.isPressed)
            {
                SpaceHeldUpdateCount++;
            }
        }

        public void ResetCount()
        {
            SpacePressedUpdateCount = 0;
            SpaceHeldUpdateCount = 0;
        }
    }

    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class FrameStateObserver : MonoBehaviour
    {
        public int WPressedUpdateCount { get; private set; }
        public int WReleasedUpdateCount { get; private set; }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.wKey.isPressed)
            {
                WPressedUpdateCount++;
                return;
            }

            WReleasedUpdateCount++;
        }

        public void ResetCounts()
        {
            WPressedUpdateCount = 0;
            WReleasedUpdateCount = 0;
        }
    }

    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class WasPressedGameplayJumpController : MonoBehaviour
    {
        private const float JumpVelocity = 8f;
        private const float SimulatedFrameSeconds = 0.016f;

        public bool Grounded { get; private set; } = true;
        public int JumpCount { get; private set; }
        public float VerticalPosition { get; private set; }
        public float VerticalVelocity { get; private set; }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (Grounded && keyboard.spaceKey.wasPressedThisFrame)
            {
                Grounded = false;
                JumpCount++;
                VerticalVelocity = JumpVelocity;
            }

            if (!Grounded)
            {
                VerticalPosition += VerticalVelocity * SimulatedFrameSeconds;
            }
        }

        public void ResetState()
        {
            Grounded = true;
            JumpCount = 0;
            VerticalPosition = 0f;
            VerticalVelocity = 0f;
        }
    }

    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class ManualModeFramePressObserver : MonoBehaviour
    {
        public int EnterPressedStateCount { get; private set; }

        private void Update()
        {
            InputSettings? settings = InputSystem.settings;
            if (settings == null || settings.updateMode != InputSettings.UpdateMode.ProcessEventsManually)
            {
                return;
            }

            InputSystem.Update();

            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.enterKey.isPressed)
            {
                EnterPressedStateCount++;
            }
        }

        public void ResetCount()
        {
            EnterPressedStateCount = 0;
        }
    }

    /// <summary>
    /// Test support type used by editor and play mode fixtures.
    /// </summary>
    public class TestableSimulateKeyboardTool : SimulateKeyboardTool
    {
        public Task<SimulateKeyboardResponse> ExecuteWithCancellationAsync(
            SimulateKeyboardSchema parameters,
            CancellationToken ct)
        {
            return ExecuteAsync(parameters, ct);
        }
    }
}
#endif
