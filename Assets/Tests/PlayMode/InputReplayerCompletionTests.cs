#if ULOOPMCP_HAS_INPUT_SYSTEM
#nullable enable
using System.Collections;
using System.Collections.Generic;
using io.github.hatayama.uLoopMCP;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace Tests.PlayMode
{
    /// <summary>
    /// Verifies that replay completion waits for UI modules to consume the final injected input frame.
    /// </summary>
    public sealed class InputReplayerCompletionTests
    {
        private const float ReplayTimeoutSeconds = 5f;
        private const float ButtonWidth = 240f;
        private const float ButtonHeight = 120f;

        private GameObject _canvasGo = null!;
        private GameObject _eventSystemGo = null!;
        private GameObject _buttonGo = null!;
        private Mouse? _createdMouse;
        private bool _replayCompleted;
        private int _clickCount;
        private int _clickCountAtCompletion;
        private bool _observedLoopFrameBeyondTotal;
        private bool _observedLoopReset;
        private int _loopObservationTicks;
        private PointerLifecycleTracker _pointerLifecycleTracker = null!;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _replayCompleted = false;
            _clickCount = 0;
            _clickCountAtCompletion = 0;
            _observedLoopFrameBeyondTotal = false;
            _observedLoopReset = false;
            _loopObservationTicks = 0;

            EnsureMouseDeviceExists();
            CreateScene();

            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            InputReplayer.ReplayCompleted -= OnReplayCompleted;
            InputSystem.onAfterUpdate -= ObserveLoopReplayCadence;

            if (InputReplayer.IsReplaying)
            {
                InputReplayer.StopReplay();
            }

            if (_createdMouse != null)
            {
                InputSystem.RemoveDevice(_createdMouse);
                _createdMouse = null;
            }

            Object.Destroy(_buttonGo);
            Object.Destroy(_eventSystemGo);
            Object.Destroy(_canvasGo);

            yield return null;
        }

        [UnityTest]
        public IEnumerator ReplayCompleted_WhenInputSystemUiConsumesTerminalRelease_FiresAfterClick()
        {
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(null, _buttonGo.transform.position);
            InputRecordingData recording = CreateTerminalClickRecording(screenPosition);

            InputReplayer.ReplayCompleted += OnReplayCompleted;
            InputReplayer.StartReplay(recording, loop: false, showOverlay: false);

            float timeoutAt = Time.realtimeSinceStartup + ReplayTimeoutSeconds;
            yield return new WaitUntil(() =>
                _replayCompleted || Time.realtimeSinceStartup >= timeoutAt);

            Assert.IsTrue(_replayCompleted, $"Replay did not complete within {ReplayTimeoutSeconds}s");
            Assert.AreEqual(1, _clickCountAtCompletion,
                "ReplayCompleted must fire after InputSystemUIInputModule processes the terminal release");
            Assert.AreEqual(1, _clickCount, "Button click must be processed exactly once");
        }

        [UnityTest]
        public IEnumerator LoopReplay_WhenInputSystemUiConsumesTerminalRelease_DoesNotInsertEmptyFrame()
        {
            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(null, _buttonGo.transform.position);
            InputRecordingData recording = CreateTerminalClickRecording(screenPosition);

            InputReplayer.StartReplay(recording, loop: true, showOverlay: false);
            InputSystem.onAfterUpdate += ObserveLoopReplayCadence;

            float timeoutAt = Time.realtimeSinceStartup + ReplayTimeoutSeconds;
            yield return new WaitUntil(() =>
                _observedLoopReset || Time.realtimeSinceStartup >= timeoutAt);

            Assert.IsTrue(_observedLoopReset, $"Looped replay did not reset within {ReplayTimeoutSeconds}s");
            Assert.IsFalse(
                _observedLoopFrameBeyondTotal,
                "Looped replay must reset on the terminal release without exposing an unrecorded empty frame");
        }

        [UnityTest]
        public IEnumerator Replay_WhenInputSystemUiModuleIsSelectedAfterManualPress_FiresMatchingPointerUp()
        {
            RecreateEventSystemWithoutSelectedInputModule();
            EventSystem? eventSystem = EventSystem.current;
            Assert.IsNotNull(eventSystem, "EventSystem.current must be assigned");
            Assert.IsNull(eventSystem!.currentInputModule, "Fresh EventSystem should not select an input module until update");

            Vector2 screenPosition = RectTransformUtility.WorldToScreenPoint(null, _buttonGo.transform.position);
            InputRecordingData recording = CreateTerminalClickRecording(screenPosition);

            InputReplayer.ReplayCompleted += OnReplayCompleted;
            InputReplayer.StartReplay(recording, loop: false, showOverlay: false);

            float timeoutAt = Time.realtimeSinceStartup + ReplayTimeoutSeconds;
            yield return new WaitUntil(() =>
                _replayCompleted || Time.realtimeSinceStartup >= timeoutAt);

            Assert.IsTrue(_replayCompleted, $"Replay did not complete within {ReplayTimeoutSeconds}s");
            Assert.Greater(_pointerLifecycleTracker.PointerDownCount, 0,
                "The replay should dispatch at least one pointer down to the target");
            Assert.AreEqual(_pointerLifecycleTracker.PointerDownCount, _pointerLifecycleTracker.PointerUpCount,
                "Every pointer down must receive a matching pointer up even if the input module is selected later");
        }

        private void OnReplayCompleted()
        {
            _clickCountAtCompletion = _clickCount;
            _replayCompleted = true;
        }

        private void ObserveLoopReplayCadence()
        {
            if (!InputReplayer.IsReplaying)
            {
                return;
            }

            if (InputReplayer.CurrentFrame > InputReplayer.TotalFrames)
            {
                _observedLoopFrameBeyondTotal = true;
            }

            if (_loopObservationTicks > 0 && InputReplayer.CurrentFrame == 0)
            {
                _observedLoopReset = true;
            }

            _loopObservationTicks++;
        }

        private void OnButtonClicked()
        {
            _clickCount++;
        }

        private void EnsureMouseDeviceExists()
        {
            if (Mouse.current != null)
            {
                return;
            }

            _createdMouse = InputSystem.AddDevice<Mouse>();
        }

        private void CreateScene()
        {
            _canvasGo = new GameObject("InputReplayerCompletionCanvas");
            Canvas canvas = _canvasGo.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            _canvasGo.AddComponent<GraphicRaycaster>();

            _eventSystemGo = new GameObject("InputReplayerCompletionEventSystem");
            _eventSystemGo.AddComponent<EventSystem>();
            _eventSystemGo.AddComponent<InputSystemUIInputModule>();

            _buttonGo = new GameObject("TerminalReleaseButton");
            _buttonGo.transform.SetParent(_canvasGo.transform, false);
            RectTransform buttonRect = _buttonGo.AddComponent<RectTransform>();
            buttonRect.sizeDelta = new Vector2(ButtonWidth, ButtonHeight);
            Image buttonImage = _buttonGo.AddComponent<Image>();
            Button button = _buttonGo.AddComponent<Button>();
            button.targetGraphic = buttonImage;
            button.onClick.AddListener(OnButtonClicked);
            _pointerLifecycleTracker = _buttonGo.AddComponent<PointerLifecycleTracker>();
        }

        private void RecreateEventSystemWithoutSelectedInputModule()
        {
            Object.DestroyImmediate(_eventSystemGo);

            _eventSystemGo = new GameObject("LateSelectedInputSystemEventSystem");
            EventSystem eventSystem = _eventSystemGo.AddComponent<EventSystem>();
            _eventSystemGo.AddComponent<InputSystemUIInputModule>();
            EventSystem.current = eventSystem;
        }

        private static InputRecordingData CreateTerminalClickRecording(Vector2 screenPosition)
        {
            string positionData = $"{Mathf.RoundToInt(screenPosition.x)},{Mathf.RoundToInt(screenPosition.y)}";

            return new InputRecordingData
            {
                Metadata = new InputRecordingMetadata
                {
                    RecordedAt = "test",
                    TotalFrames = 1,
                    DurationSeconds = 0.02f
                },
                Frames = new List<InputFrameEvents>
                {
                    new InputFrameEvents
                    {
                        Frame = 0,
                        Events = new List<RecordedInputEvent>
                        {
                            new RecordedInputEvent { Type = InputEventTypes.MOUSE_POSITION, Data = positionData },
                            new RecordedInputEvent { Type = InputEventTypes.MOUSE_CLICK, Data = "Left" }
                        }
                    },
                    new InputFrameEvents
                    {
                        Frame = 1,
                        Events = new List<RecordedInputEvent>
                        {
                            new RecordedInputEvent { Type = InputEventTypes.MOUSE_POSITION, Data = positionData },
                            new RecordedInputEvent { Type = InputEventTypes.MOUSE_RELEASE, Data = "Left" }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Counts pointer lifecycle events sent to the replay target.
        /// </summary>
        private sealed class PointerLifecycleTracker : MonoBehaviour, IPointerDownHandler, IPointerUpHandler
        {
            public int PointerDownCount { get; private set; }
            public int PointerUpCount { get; private set; }

            public void OnPointerDown(PointerEventData eventData)
            {
                PointerDownCount++;
            }

            public void OnPointerUp(PointerEventData eventData)
            {
                PointerUpCount++;
            }
        }
    }
}
#endif
