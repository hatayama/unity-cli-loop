using System;
using NUnit.Framework;
using UnityEditor;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests that the control-play-mode editor state service follows the active Play Mode configuration.
    /// </summary>
    public sealed class ControlPlayModeEditorStateServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        [TearDown]
        public void TearDown()
        {
            PlayModeStopReasonSessionStore.ClearForTests();
        }

        /// <summary>
        /// What: Play with a non-default configuration active starts it instead of writing EditorApplication.isPlaying.
        /// </summary>
        [Test]
        public void IsPlaying_SetTrue_WhenNonDefaultScenarioActive_StartsScenarioInsteadOfEditorApplication()
        {
            Assert.That(EditorApplication.isPlaying, Is.False);
            FakePlayModeScenarioBridge bridge = new FakePlayModeScenarioBridge(
                isNonDefaultScenarioActive: true,
                isScenarioRunning: false,
                activeScenarioName: "SampleScenario");
            ControlPlayModeEditorStateService service = new ControlPlayModeEditorStateService(bridge);

            service.IsPlaying = true;

            Assert.That(bridge.StartCallCount, Is.EqualTo(1));
            Assert.That(EditorApplication.isPlaying, Is.False);
        }

        /// <summary>
        /// What: Stop with a running non-default configuration stops it and records the CLI stop reason.
        /// </summary>
        [Test]
        public void IsPlaying_SetFalse_WhenNonDefaultScenarioActive_StopsScenarioAndRecordsCliStopReason()
        {
            FakePlayModeScenarioBridge bridge = new FakePlayModeScenarioBridge(
                isNonDefaultScenarioActive: true,
                isScenarioRunning: true,
                activeScenarioName: "SampleScenario");
            ControlPlayModeEditorStateService service = new ControlPlayModeEditorStateService(bridge);

            service.IsPlaying = false;

            Assert.That(bridge.StopCallCount, Is.EqualTo(1));
            Assert.That(
                PlayModeStopReasonSessionStore.PendingReason,
                Is.EqualTo("cli-control-play-mode"));
        }

        /// <summary>
        /// What: the active scenario name is taken from the bridge when a non-default configuration is active.
        /// </summary>
        [Test]
        public void ActiveScenarioName_WhenBridgeReportsScenario_ReturnsThatName()
        {
            FakePlayModeScenarioBridge bridge = new FakePlayModeScenarioBridge(
                isNonDefaultScenarioActive: true,
                isScenarioRunning: false,
                activeScenarioName: "SampleScenario");
            ControlPlayModeEditorStateService service = new ControlPlayModeEditorStateService(bridge);

            Assert.That(service.ActiveScenarioName, Is.EqualTo("SampleScenario"));
        }

        /// <summary>
        /// What: no scenario name is reported while the default configuration is active.
        /// </summary>
        [Test]
        public void ActiveScenarioName_WhenDefaultConfiguration_ReturnsNull()
        {
            FakePlayModeScenarioBridge bridge = new FakePlayModeScenarioBridge(
                isNonDefaultScenarioActive: false,
                isScenarioRunning: false,
                activeScenarioName: null);
            ControlPlayModeEditorStateService service = new ControlPlayModeEditorStateService(bridge);

            Assert.That(service.ActiveScenarioName, Is.Null);
        }

        /// <summary>
        /// What: the reflection bridge reports no scenario in a project without an active non-default configuration.
        /// </summary>
        [Test]
        public void ReflectionBridge_InProjectWithoutActiveScenario_ReportsNoScenario()
        {
            PlayModeManagerReflectionBridge bridge = new PlayModeManagerReflectionBridge();

            Assert.That(bridge.IsNonDefaultScenarioActive, Is.False);
            Assert.That(bridge.ActiveScenarioName, Is.Null);
        }

        /// <summary>
        /// What: the reflection bridge rejects Start and Stop when no non-default configuration is active.
        /// </summary>
        [Test]
        public void ReflectionBridge_Start_WhenNoScenarioActive_ThrowsInvalidOperation()
        {
            PlayModeManagerReflectionBridge bridge = new PlayModeManagerReflectionBridge();

            Assert.Throws<InvalidOperationException>(() => bridge.Start());
            Assert.Throws<InvalidOperationException>(() => bridge.Stop());
            Assert.That(bridge.IsScenarioRunning, Is.False);
        }

        private sealed class FakePlayModeScenarioBridge : IPlayModeScenarioBridge
        {
            public FakePlayModeScenarioBridge(
                bool isNonDefaultScenarioActive,
                bool isScenarioRunning,
                string activeScenarioName)
            {
                IsNonDefaultScenarioActive = isNonDefaultScenarioActive;
                IsScenarioRunning = isScenarioRunning;
                ActiveScenarioName = activeScenarioName;
            }

            public bool IsNonDefaultScenarioActive { get; }
            public bool IsScenarioRunning { get; }
            public string ActiveScenarioName { get; }
            public int StartCallCount { get; private set; }
            public int StopCallCount { get; private set; }

            public void Start()
            {
                StartCallCount++;
            }

            public void Stop()
            {
                StopCallCount++;
            }
        }
    }
}
