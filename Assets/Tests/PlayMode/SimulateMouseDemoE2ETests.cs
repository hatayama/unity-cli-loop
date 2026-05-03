#if ULOOP_HAS_INPUT_SYSTEM
using System.Collections;
using System.IO;
using io.github.hatayama.UnityCliLoop;
using NUnit.Framework;
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

        private bool _replayCompleted;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _replayCompleted = false;
            InputReplayer.ReplayCompleted += OnReplayCompleted;

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
                Object.FindAnyObjectByType<ReplayVerificationControllerBase>();
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
    }
}
#endif
