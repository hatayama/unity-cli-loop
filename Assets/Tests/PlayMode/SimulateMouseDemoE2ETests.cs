#if ULOOPMCP_HAS_INPUT_SYSTEM
using System.Collections;
using System.IO;
using System.Reflection;
using io.github.hatayama.uLoopMCP;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace Tests.PlayMode
{
    public class SimulateMouseDemoE2ETests
    {
        private const string SCENE_PATH = "Assets/Scenes/SimulateMouseDemoScene.unity";
        private const string FIXTURE_DIR = "Assets/Tests/PlayMode/Fixtures/SimulateMouseDemoScene";
        private const float REPLAY_TIMEOUT_SECONDS = 30f;
        private const int FULL_HD_WIDTH = 1920;
        private const int FULL_HD_HEIGHT = 1080;
        private const string FULL_HD_LABEL = "uLoop E2E Full HD";

        private bool _replayCompleted;
        private int _previousGameViewSizeIndex = -1;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _replayCompleted = false;
            InputReplayer.ReplayCompleted += OnReplayCompleted;
            SetGameViewResolution();

            AsyncOperation loadOp = EditorSceneManager.LoadSceneAsyncInPlayMode(
                SCENE_PATH,
                new LoadSceneParameters(LoadSceneMode.Single));

            while (!loadOp.isDone)
            {
                yield return null;
            }

            AssertGameViewResolution();

            // EditorBridge [InitializeOnLoad] subscribes on the first frame after load;
            // second yield ensures its event hooks are active before replay starts.
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            InputReplayer.ReplayCompleted -= OnReplayCompleted;

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

            RestoreGameViewResolution();

            yield return null;
        }

        [UnityTest]
        public IEnumerator Replay_Should_ProduceIdenticalEventLog()
        {
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

            InputReplayer.StartReplay(recordingData, loop: false, showOverlay: true);

            float timeoutAt = Time.realtimeSinceStartup + REPLAY_TIMEOUT_SECONDS;
            yield return new WaitUntil(() =>
                _replayCompleted || Time.realtimeSinceStartup >= timeoutAt);

            Assert.IsTrue(_replayCompleted,
                $"Replay did not complete within {REPLAY_TIMEOUT_SECONDS}s");

            // OnCompareLogs runs synchronously inside ReplayCompleted but
            // LastComparisonDiffCount must be read after the event dispatch completes.
            yield return null;

            ReplayVerificationControllerBase controller =
                Object.FindAnyObjectByType<ReplayVerificationControllerBase>();
            Assert.IsNotNull(controller, "Scene must contain a ReplayVerificationControllerBase");
            Assert.AreEqual(0, controller.LastComparisonDiffCount,
                $"Replay event log should match expected. Diff count: {controller.LastComparisonDiffCount}");
        }

        private void OnReplayCompleted()
        {
            _replayCompleted = true;
        }

        private void SetGameViewResolution()
        {
            System.Type gameViewType = GetGameViewType();
            EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
            _previousGameViewSizeIndex = GetSelectedGameViewSizeIndex(gameViewType, gameView);

            MethodInfo setCustomResolution = gameViewType.GetMethod(
                "SetCustomResolution",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Debug.Assert(setCustomResolution != null, "GameView.SetCustomResolution must exist");

            // Unity exposes no public API for Game View target resolution, but this E2E
            // fixture must replay against the same pixel size as the recorded mouse positions.
            setCustomResolution.Invoke(
                gameView,
                new object[] { new Vector2(FULL_HD_WIDTH, FULL_HD_HEIGHT), FULL_HD_LABEL });
        }

        private void RestoreGameViewResolution()
        {
            if (_previousGameViewSizeIndex < 0)
            {
                return;
            }

            System.Type gameViewType = GetGameViewType();
            EditorWindow gameView = EditorWindow.GetWindow(gameViewType);
            SetSelectedGameViewSizeIndex(gameViewType, gameView, _previousGameViewSizeIndex);
            UpdateGameViewZoom(gameViewType, gameView);
            gameView.Repaint();
            SceneView.RepaintAll();
            _previousGameViewSizeIndex = -1;
        }

        private static System.Type GetGameViewType()
        {
            System.Type gameViewType = typeof(Editor).Assembly.GetType("UnityEditor.GameView");
            Debug.Assert(gameViewType != null, "GameView type must exist");
            return gameViewType;
        }

        private static int GetSelectedGameViewSizeIndex(System.Type gameViewType, EditorWindow gameView)
        {
            MethodInfo getSelectedSizeIndex = gameViewType.GetMethod(
                "get_selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Debug.Assert(getSelectedSizeIndex != null, "GameView.get_selectedSizeIndex must exist");

            object selectedSizeIndex = getSelectedSizeIndex.Invoke(gameView, null);
            Debug.Assert(selectedSizeIndex is int, "GameView selected size index must be an int");
            return (int)selectedSizeIndex;
        }

        private static void SetSelectedGameViewSizeIndex(
            System.Type gameViewType,
            EditorWindow gameView,
            int selectedSizeIndex)
        {
            MethodInfo setSelectedSizeIndex = gameViewType.GetMethod(
                "set_selectedSizeIndex",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Debug.Assert(setSelectedSizeIndex != null, "GameView.set_selectedSizeIndex must exist");

            // Game View size is editor-persistent; restore it so this fixed-resolution E2E test stays isolated.
            setSelectedSizeIndex.Invoke(gameView, new object[] { selectedSizeIndex });
        }

        private static void UpdateGameViewZoom(System.Type gameViewType, EditorWindow gameView)
        {
            MethodInfo updateZoomArea = gameViewType.GetMethod(
                "UpdateZoomAreaAndParent",
                BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public);
            Debug.Assert(updateZoomArea != null, "GameView.UpdateZoomAreaAndParent must exist");

            updateZoomArea.Invoke(gameView, null);
        }

        private static void AssertGameViewResolution()
        {
            Vector2 size = Handles.GetMainGameViewSize();
            Assert.AreEqual(FULL_HD_WIDTH, Mathf.RoundToInt(size.x), "Game View width must match fixture recording");
            Assert.AreEqual(FULL_HD_HEIGHT, Mathf.RoundToInt(size.y), "Game View height must match fixture recording");
        }

        private static void CleanupLogFile(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
#endif
