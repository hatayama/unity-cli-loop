#if ULOOP_HAS_INPUT_SYSTEM
using System;
using System.Collections;
using System.IO;
using System.Reflection;
using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Tests.Demo;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace io.github.hatayama.UnityCliLoop.Tests.PlayMode
{
    /// <summary>
    /// Test fixture that verifies Simulate Mouse Demo 2 E behavior.
    /// </summary>
    public class SimulateMouseDemoE2ETests
    {
        private const string SCENE_PATH = "Assets/Scenes/SimulateMouseDemoScene.unity";
        private const string FIXTURE_DIR = "Assets/Tests/PlayMode/Fixtures/SimulateMouseDemoScene";
        private const string FIXTURE_GAME_VIEW_SIZE = "2048x1152";
        private const float REPLAY_TIMEOUT_SECONDS = 30f;

        private bool _replayCompleted;
        private GameViewSizeFixture _gameViewSizeFixture;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _replayCompleted = false;
            InputReplayer.AddReplayCompletedHandler(OnReplayCompleted);
            _gameViewSizeFixture = new GameViewSizeFixture(FIXTURE_GAME_VIEW_SIZE);

            AsyncOperation loadOp = EditorSceneManager.LoadSceneAsyncInPlayMode(
                SCENE_PATH,
                new LoadSceneParameters(LoadSceneMode.Single));

            while (!loadOp.isDone)
            {
                yield return null;
            }

            // EditorBridge [InitializeOnLoad] subscribes on the first frame after load;
            // second yield ensures its event hooks are active before replay starts.
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            InputReplayer.RemoveReplayCompletedHandler(OnReplayCompleted);

            if (InputReplayer.IsReplaying)
            {
                InputReplayer.StopReplay();
            }

            CleanupLogFile(Path.Combine(
                ReplayVerificationControllerBase.LOG_OUTPUT_DIR,
                ReplayVerificationControllerBase.RECORDING_LOG_FILE));
            CleanupLogFile(Path.Combine(
                ReplayVerificationControllerBase.LOG_OUTPUT_DIR,
                ReplayVerificationControllerBase.REPLAY_LOG_FILE));

            if (_gameViewSizeFixture != null)
            {
                _gameViewSizeFixture.Restore();
                _gameViewSizeFixture = null;
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator Replay_Should_ProduceIdenticalEventLog()
        {
            // Verifies that the UI replay fixture reproduces the recorded mouse-driven UI log.
            string fixtureRecordingJson = Path.Combine(FIXTURE_DIR, "recording.json");
            string fixtureExpectedLog = Path.Combine(FIXTURE_DIR, "expected-event-log.txt");

            Assert.IsTrue(File.Exists(fixtureRecordingJson),
                $"Fixture recording JSON not found: {fixtureRecordingJson}");
            Assert.IsTrue(File.Exists(fixtureExpectedLog),
                $"Fixture expected event log not found: {fixtureExpectedLog}");

            // OnCompareLogs() expects recording-event-log.txt to already exist as golden reference
            string targetRecordingLogPath = Path.Combine(
                ReplayVerificationControllerBase.LOG_OUTPUT_DIR,
                ReplayVerificationControllerBase.RECORDING_LOG_FILE);
            Directory.CreateDirectory(ReplayVerificationControllerBase.LOG_OUTPUT_DIR);
            File.Copy(fixtureExpectedLog, targetRecordingLogPath, true);

            InputRecordingData recordingData = InputRecordingFileHelper.Load(fixtureRecordingJson);
            Debug.Assert(recordingData != null, $"Failed to load fixture: {fixtureRecordingJson}");

            InputReplayer.StartReplay(recordingData, loop: false, showOverlay: false);

            float timeoutAt = Time.realtimeSinceStartup + REPLAY_TIMEOUT_SECONDS;
            yield return new WaitUntil(() =>
                _replayCompleted || Time.realtimeSinceStartup >= timeoutAt);

            Assert.IsTrue(_replayCompleted,
                $"Replay did not complete within {REPLAY_TIMEOUT_SECONDS}s");

            // OnCompareLogs runs synchronously inside ReplayCompleted but
            // LastComparisonDiffCount must be read after the event dispatch completes.
            yield return null;

            ReplayVerificationControllerBase controller =
                UnityEngine.Object.FindAnyObjectByType<ReplayVerificationControllerBase>();
            Assert.IsNotNull(controller, "Scene must contain a ReplayVerificationControllerBase");
            Assert.AreEqual(0, controller.LastComparisonDiffCount,
                $"Replay event log should match expected. Diff count: {controller.LastComparisonDiffCount}");
        }

        private void OnReplayCompleted()
        {
            _replayCompleted = true;
        }

        private static void CleanupLogFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        // Keeps Game View resolution-dependent UI replay fixtures deterministic.
        // Unity's Game View size dropdown has no public setter, so the helper uses
        // the same internal editor boundary already used by this project for Game View capture.
        /// <summary>
        /// Test support type used by editor and play mode fixtures.
        /// </summary>
        private sealed class GameViewSizeFixture
        {
            private readonly int _originalSelectedSizeIndex;
            private bool _restored;

            public GameViewSizeFixture(string requiredDisplayText)
            {
                Type gameViewType = GetEditorType("UnityEditor.GameView");
                EditorWindow gameView = GetMainGameView(gameViewType);
                PropertyInfo selectedSizeIndexProperty = GetSelectedSizeIndexProperty(gameViewType);

                _originalSelectedSizeIndex = (int)selectedSizeIndexProperty.GetValue(gameView, null);

                object sizeGroup = GetStandaloneSizeGroup();
                string[] displayTexts = GetDisplayTexts(sizeGroup);
                int fixtureSizeIndex = FindSizeIndex(displayTexts, requiredDisplayText);
                if (fixtureSizeIndex < 0)
                {
                    AddFixedResolutionSize(sizeGroup, requiredDisplayText);
                    displayTexts = GetDisplayTexts(sizeGroup);
                    fixtureSizeIndex = FindSizeIndex(displayTexts, requiredDisplayText);
                }

                Assert.GreaterOrEqual(fixtureSizeIndex, 0, $"Game View size containing '{requiredDisplayText}' must exist for the replay fixture.");

                selectedSizeIndexProperty.SetValue(gameView, fixtureSizeIndex, null);
                gameView.Repaint();
            }

            public void Restore()
            {
                if (_restored)
                {
                    return;
                }

                Type gameViewType = GetEditorType("UnityEditor.GameView");
                EditorWindow gameView = GetMainGameView(gameViewType);
                PropertyInfo selectedSizeIndexProperty = GetSelectedSizeIndexProperty(gameViewType);
                selectedSizeIndexProperty.SetValue(gameView, _originalSelectedSizeIndex, null);
                gameView.Repaint();
                _restored = true;
            }

            private static Type GetEditorType(string typeName)
            {
                Type type = typeof(Editor).Assembly.GetType(typeName);
                Assert.IsNotNull(type, $"{typeName} must exist in the Unity editor assembly.");
                return type;
            }

            private static EditorWindow GetMainGameView(Type gameViewType)
            {
                UnityEngine.Object[] gameViews = Resources.FindObjectsOfTypeAll(gameViewType);
                for (int i = 0; i < gameViews.Length; i++)
                {
                    EditorWindow candidate = gameViews[i] as EditorWindow;
                    if (candidate != null && candidate.hasFocus)
                    {
                        return candidate;
                    }
                }

                if (gameViews.Length > 0)
                {
                    EditorWindow existingWindow = gameViews[0] as EditorWindow;
                    Assert.IsNotNull(existingWindow, "Existing Game View object must be an EditorWindow.");
                    return existingWindow;
                }

                EditorWindow createdWindow = EditorWindow.GetWindow(gameViewType);
                Assert.IsNotNull(createdWindow, "Game View window must be available.");
                return createdWindow;
            }

            private static PropertyInfo GetSelectedSizeIndexProperty(Type gameViewType)
            {
                PropertyInfo property = gameViewType.GetProperty(
                    "selectedSizeIndex",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.IsNotNull(property, "Game View selectedSizeIndex property must exist.");
                return property;
            }

            private static object GetStandaloneSizeGroup()
            {
                Type gameViewSizesType = GetEditorType("UnityEditor.GameViewSizes");
                Type singletonType = typeof(ScriptableSingleton<>).MakeGenericType(gameViewSizesType);
                PropertyInfo instanceProperty = singletonType.GetProperty(
                    "instance",
                    BindingFlags.Public | BindingFlags.Static);
                Assert.IsNotNull(instanceProperty, "GameViewSizes instance property must exist.");

                object gameViewSizes = instanceProperty.GetValue(null, null);
                Assert.IsNotNull(gameViewSizes, "GameViewSizes singleton must exist.");

                MethodInfo getGroupMethod = gameViewSizesType.GetMethod(
                    "GetGroup",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.IsNotNull(getGroupMethod, "GameViewSizes.GetGroup method must exist.");

                object sizeGroup = getGroupMethod.Invoke(
                    gameViewSizes,
                    new object[] { GameViewSizeGroupType.Standalone });
                Assert.IsNotNull(sizeGroup, "Standalone Game View size group must exist.");
                return sizeGroup;
            }

            private static string[] GetDisplayTexts(object sizeGroup)
            {
                MethodInfo getDisplayTextsMethod = sizeGroup.GetType().GetMethod(
                    "GetDisplayTexts",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.IsNotNull(getDisplayTextsMethod, "GameViewSizeGroup.GetDisplayTexts method must exist.");

                object displayTexts = getDisplayTextsMethod.Invoke(sizeGroup, null);
                string[] texts = displayTexts as string[];
                Assert.IsNotNull(texts, "Game View display texts must be a string array.");
                return texts;
            }

            private static int FindSizeIndex(string[] displayTexts, string requiredDisplayText)
            {
                for (int i = 0; i < displayTexts.Length; i++)
                {
                    if (displayTexts[i].Contains(requiredDisplayText))
                    {
                        return i;
                    }
                }

                return -1;
            }

            private static void AddFixedResolutionSize(object sizeGroup, string requiredDisplayText)
            {
                string[] dimensions = requiredDisplayText.Split('x');
                Assert.That(dimensions, Has.Length.EqualTo(2), "Fixture Game View size must use WIDTHxHEIGHT format.");
                bool parsedWidth = int.TryParse(dimensions[0], out int width);
                bool parsedHeight = int.TryParse(dimensions[1], out int height);
                Assert.IsTrue(parsedWidth, "Fixture Game View width must be numeric.");
                Assert.IsTrue(parsedHeight, "Fixture Game View height must be numeric.");

                Type gameViewSizeType = GetEditorType("UnityEditor.GameViewSize");
                Type gameViewSizeKindType = GetEditorType("UnityEditor.GameViewSizeType");
                object fixedResolution = Enum.Parse(gameViewSizeKindType, "FixedResolution");
                object gameViewSize = Activator.CreateInstance(
                    gameViewSizeType,
                    fixedResolution,
                    width,
                    height,
                    requiredDisplayText);
                Assert.IsNotNull(gameViewSize, "Game View custom size instance must be created.");

                MethodInfo addCustomSizeMethod = sizeGroup.GetType().GetMethod(
                    "AddCustomSize",
                    BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.IsNotNull(addCustomSizeMethod, "GameViewSizeGroup.AddCustomSize method must exist.");
                addCustomSizeMethod.Invoke(sizeGroup, new object[] { gameViewSize });
            }
        }
    }
}
#endif
