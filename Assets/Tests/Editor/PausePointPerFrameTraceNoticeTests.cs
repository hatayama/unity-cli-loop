using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the enable-time notice when a trace marker sits in a per-frame Unity message.
    /// </summary>
    [TestFixture]
    public sealed class PausePointPerFrameTraceNoticeTests
    {
        private const string FixtureFilePath = "Assets/Tests/Editor/PausePointPerFrameTraceNoticeFixture.cs";

        private const string PlayerUpdateNotice =
            "'Player.Update' is a per-frame Unity message; with capture mode 'trace' the history (max 8) can roll over within moments. Prefer a conditional line or a larger --max-history.";

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            SourcePausePointPatcher.UnpatchAll();
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// What: trace on Update / FixedUpdate / LateUpdate / OnGUI appends the per-frame notice.
        /// </summary>
        [TestCase(
            "Player.Update",
            "'Player.Update' is a per-frame Unity message; with capture mode 'trace' the history (max 8) can roll over within moments. Prefer a conditional line or a larger --max-history.")]
        [TestCase(
            "Player.FixedUpdate",
            "'Player.FixedUpdate' is a per-frame Unity message; with capture mode 'trace' the history (max 8) can roll over within moments. Prefer a conditional line or a larger --max-history.")]
        [TestCase(
            "Player.LateUpdate",
            "'Player.LateUpdate' is a per-frame Unity message; with capture mode 'trace' the history (max 8) can roll over within moments. Prefer a conditional line or a larger --max-history.")]
        [TestCase(
            "Player.OnGUI",
            "'Player.OnGUI' is a per-frame Unity message; with capture mode 'trace' the history (max 8) can roll over within moments. Prefer a conditional line or a larger --max-history.")]
        public void BuildPerFrameTraceWarningOrEmpty_WhenTraceAndPerFrameMessage_ReturnsNotice(
            string resolvedMethod,
            string expectedNotice)
        {
            string warning = PausePointEnableWarnings.BuildPerFrameTraceWarningOrEmpty(
                UloopPausePointCaptureMode.Trace,
                resolvedMethod,
                8);

            Assert.That(warning, Is.EqualTo(expectedNotice));
        }

        /// <summary>
        /// What: a Cecil FullName resolved method still interpolates Type.Method into the notice.
        /// </summary>
        [Test]
        public void BuildPerFrameTraceWarningOrEmpty_WhenResolvedMethodIsCecilFullName_FormatsTypeMethod()
        {
            string warning = PausePointEnableWarnings.BuildPerFrameTraceWarningOrEmpty(
                UloopPausePointCaptureMode.Trace,
                "System.Void Ns.Player::Update()",
                8);

            Assert.That(warning, Is.EqualTo(PlayerUpdateNotice));
        }

        /// <summary>
        /// What: non-trace capture modes stay silent even when the method is a per-frame Unity message.
        /// </summary>
        [TestCase(UloopPausePointCaptureMode.SingleShot)]
        [TestCase(UloopPausePointCaptureMode.Continuous)]
        public void BuildPerFrameTraceWarningOrEmpty_WhenModeIsNotTrace_ReturnsEmpty(string captureMode)
        {
            string warning = PausePointEnableWarnings.BuildPerFrameTraceWarningOrEmpty(
                captureMode,
                "Player.Update",
                8);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a method whose simple name is not a per-frame Unity message stays silent in trace.
        /// </summary>
        [TestCase("Player.Start")]
        [TestCase("Player.UpdateWeather")]
        [TestCase("Player.OnGUILayout")]
        public void BuildPerFrameTraceWarningOrEmpty_WhenSimpleNameIsNotPerFrame_ReturnsEmpty(string resolvedMethod)
        {
            string warning = PausePointEnableWarnings.BuildPerFrameTraceWarningOrEmpty(
                UloopPausePointCaptureMode.Trace,
                resolvedMethod,
                8);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: the per-frame notice is appended after an existing enable warning.
        /// </summary>
        [Test]
        public void MergeWarnings_WhenPriorWarningAndPerFrameNotice_AppendsNoticeAfterPrior()
        {
            string prior = "Pause point was enabled before PlayMode while Domain Reload is enabled. Entering PlayMode may clear this marker; keep Domain Reload disabled for this workflow or enable the marker after PlayMode starts.";
            string notice = PausePointEnableWarnings.BuildPerFrameTraceWarningOrEmpty(
                UloopPausePointCaptureMode.Trace,
                "Player.Update",
                8);

            string merged = PausePointEnableWarnings.MergeWarnings(prior, notice);

            Assert.That(
                merged,
                Is.EqualTo(
                    "Pause point was enabled before PlayMode while Domain Reload is enabled. Entering PlayMode may clear this marker; keep Domain Reload disabled for this workflow or enable the marker after PlayMode starts. 'Player.Update' is a per-frame Unity message; with capture mode 'trace' the history (max 8) can roll over within moments. Prefer a conditional line or a larger --max-history."));
        }

        /// <summary>
        /// What: enable with file:line trace on Update appends the per-frame notice using the effective max-history.
        /// </summary>
        [Test]
        public void Enable_WhenTraceMarkerIsOnUpdate_AppendsPerFrameNotice()
        {
            string absolutePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), FixtureFilePath);
            string diskSource = File.ReadAllText(absolutePath);
            int requestedLine = FindLineNumberContaining(diskSource, "per-frame-trace-notice" + "-probe-unique") + 1;
            Assert.That(requestedLine, Is.GreaterThan(1));

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureFilePath,
                Line = requestedLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.Trace,
                MaxHistory = 8
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message + " / " + response.RecommendedNextAction);
            string notice =
                "'PerFrameTraceNoticeFixture.Update' is a per-frame Unity message; with capture mode 'trace' the history (max 8) can roll over within moments. Prefer a conditional line or a larger --max-history.";
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.CreateEnableWarning(),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning),
                notice);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
        }

        private static int FindLineNumberContaining(string source, string fragment)
        {
            string[] lines = source.Replace("\r\n", "\n").Split('\n');
            for (int index = 0; index < lines.Length; index++)
            {
                if (lines[index].Contains(fragment))
                {
                    return index + 1;
                }
            }

            return -1;
        }

        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public int PauseCount { get; private set; }
            public bool IsPlaying => true;
            public bool IsPaused => PauseCount > 0;

            public void Pause()
            {
                PauseCount++;
            }

            public void Resume()
            {
                PauseCount = 0;
            }
        }
    }
}
