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
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;
using UnityEngine.TestTools;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Test fixture that verifies Simulate Keyboard behavior.
    /// </summary>
    public class SimulateKeyboardTests : InputTestFixture
    {
        private GameObject eventSystemGo = null!;
        private GameObject framePressObserverGo = null!;
        private TestableSimulateKeyboardTool tool = null!;
        private SimulateKeyboardResponse lastResponse = null!;
        private Keyboard keyboard = null!;
        private FramePressObserver framePressObserver = null!;
        private FrameStateObserver frameStateObserver = null!;
        private ManualModeFramePressObserver manualModeFramePressObserver = null!;
        private InputSettings.UpdateMode originalUpdateMode;
        private float originalTimeScale;

        public override void Setup()
        {
            base.Setup();
            InputSettings settings = RequireInputSettings();
            originalUpdateMode = settings.updateMode;
            originalTimeScale = Time.timeScale;

            eventSystemGo = new GameObject("TestEventSystem");
            eventSystemGo.AddComponent<EventSystem>();
            framePressObserverGo = new GameObject("FramePressObserver");
            framePressObserver = framePressObserverGo.AddComponent<FramePressObserver>();
            frameStateObserver = framePressObserverGo.AddComponent<FrameStateObserver>();
            manualModeFramePressObserver = framePressObserverGo.AddComponent<ManualModeFramePressObserver>();

            tool = new TestableSimulateKeyboardTool();
            keyboard = InputSystem.AddDevice<Keyboard>();
        }

        public override void TearDown()
        {
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

            SimulateKeyboardSchema parameters = new()            {
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

        #endregion

        #region Helpers

        private IEnumerator RunTool(JObject parameters)
        {
            Task<UnityCliLoopToolResponse> task = tool.ExecuteAsync(parameters);
            yield return WaitForTask(task);
            lastResponse = (SimulateKeyboardResponse)task.Result;
        }

        private static IEnumerator WaitForTask(Task task, bool allowCanceled = false)
        {
            float timeoutAt = Time.realtimeSinceStartup + 5f;
            yield return new WaitUntil(() =>
                task.IsCompleted || Time.realtimeSinceStartup >= timeoutAt);
            Assert.IsTrue(task.IsCompleted, "Tool execution timed out.");
            if (!allowCanceled)
            {
                Assert.IsFalse(task.IsCanceled, "Tool execution should not be canceled.");
            }
            Assert.IsFalse(task.IsFaulted, $"Tool execution should not fault: {task.Exception}");
        }

        private static InputSettings RequireInputSettings()
        {
            InputSettings? settings = InputSystem.settings;
            Debug.Assert(settings != null, "InputSystem.settings must be available in SimulateKeyboardTests");
            return settings!;
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
