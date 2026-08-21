using System;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the enable-time notice when a non-trace marker's method name matches a per-frame Unity message.
    /// </summary>
    [TestFixture]
    public sealed class PausePointPerFrameImmediateHitNoticeTests
    {
        private const string FixtureFilePath = "Assets/Tests/Editor/PausePointPerFrameTraceNoticeFixture.cs";

        private const string PlayerUpdateNotice =
            "'Player.Update' matches a per-frame Unity message name; if the resolved line executes unconditionally, the marker hits on the next frame, before the input or event you meant to observe arrives. Prefer a line that only executes when that event happens (inside its guarding if), or hold the input down with simulate-keyboard KeyDown before arming.";

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
        /// What: SingleShot / Continuous on Update / FixedUpdate / LateUpdate / OnGUI returns the immediate-hit notice.
        /// </summary>
        [TestCase(
            UloopPausePointCaptureMode.SingleShot,
            "Player.Update",
            "'Player.Update' matches a per-frame Unity message name; if the resolved line executes unconditionally, the marker hits on the next frame, before the input or event you meant to observe arrives. Prefer a line that only executes when that event happens (inside its guarding if), or hold the input down with simulate-keyboard KeyDown before arming.")]
        [TestCase(
            UloopPausePointCaptureMode.Continuous,
            "Player.Update",
            "'Player.Update' matches a per-frame Unity message name; if the resolved line executes unconditionally, the marker hits on the next frame, before the input or event you meant to observe arrives. Prefer a line that only executes when that event happens (inside its guarding if), or hold the input down with simulate-keyboard KeyDown before arming.")]
        [TestCase(
            UloopPausePointCaptureMode.SingleShot,
            "Player.FixedUpdate",
            "'Player.FixedUpdate' matches a per-frame Unity message name; if the resolved line executes unconditionally, the marker hits on the next frame, before the input or event you meant to observe arrives. Prefer a line that only executes when that event happens (inside its guarding if), or hold the input down with simulate-keyboard KeyDown before arming.")]
        [TestCase(
            UloopPausePointCaptureMode.SingleShot,
            "Player.LateUpdate",
            "'Player.LateUpdate' matches a per-frame Unity message name; if the resolved line executes unconditionally, the marker hits on the next frame, before the input or event you meant to observe arrives. Prefer a line that only executes when that event happens (inside its guarding if), or hold the input down with simulate-keyboard KeyDown before arming.")]
        [TestCase(
            UloopPausePointCaptureMode.SingleShot,
            "Player.OnGUI",
            "'Player.OnGUI' matches a per-frame Unity message name; if the resolved line executes unconditionally, the marker hits on the next frame, before the input or event you meant to observe arrives. Prefer a line that only executes when that event happens (inside its guarding if), or hold the input down with simulate-keyboard KeyDown before arming.")]
        public void BuildPerFrameImmediateHitWarningOrEmpty_WhenNonTraceAndPerFrameMessage_ReturnsNotice(
            string captureMode,
            string resolvedMethod,
            string expectedNotice)
        {
            string warning = PausePointPerFrameEnableWarnings.BuildPerFrameImmediateHitWarningOrEmpty(
                captureMode,
                resolvedMethod);

            Assert.That(warning, Is.EqualTo(expectedNotice));
        }

        /// <summary>
        /// What: a Cecil FullName resolved method still interpolates Type.Method into the notice.
        /// </summary>
        [Test]
        public void BuildPerFrameImmediateHitWarningOrEmpty_WhenResolvedMethodIsCecilFullName_FormatsTypeMethod()
        {
            string warning = PausePointPerFrameEnableWarnings.BuildPerFrameImmediateHitWarningOrEmpty(
                UloopPausePointCaptureMode.SingleShot,
                "System.Void Ns.Player::Update()");

            Assert.That(warning, Is.EqualTo(PlayerUpdateNotice));
        }

        /// <summary>
        /// What: trace mode stays silent so the existing per-frame trace notice remains exclusive.
        /// </summary>
        [Test]
        public void BuildPerFrameImmediateHitWarningOrEmpty_WhenModeIsTrace_ReturnsEmpty()
        {
            string warning = PausePointPerFrameEnableWarnings.BuildPerFrameImmediateHitWarningOrEmpty(
                UloopPausePointCaptureMode.Trace,
                "Player.Update");

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: an empty resolved method stays silent.
        /// </summary>
        [Test]
        public void BuildPerFrameImmediateHitWarningOrEmpty_WhenResolvedMethodIsEmpty_ReturnsEmpty()
        {
            string warning = PausePointPerFrameEnableWarnings.BuildPerFrameImmediateHitWarningOrEmpty(
                UloopPausePointCaptureMode.SingleShot,
                string.Empty);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a method whose simple name is not a per-frame Unity message stays silent.
        /// </summary>
        [TestCase("Player.Start")]
        [TestCase("Player.UpdateWeather")]
        [TestCase("Player.OnGUILayout")]
        public void BuildPerFrameImmediateHitWarningOrEmpty_WhenSimpleNameIsNotPerFrame_ReturnsEmpty(
            string resolvedMethod)
        {
            string warning = PausePointPerFrameEnableWarnings.BuildPerFrameImmediateHitWarningOrEmpty(
                UloopPausePointCaptureMode.SingleShot,
                resolvedMethod);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: enable with file:line SingleShot on Update appends the immediate-hit notice.
        /// </summary>
        [Test]
        public void Enable_WhenSingleShotMarkerIsOnUpdate_AppendsImmediateHitNotice()
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
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message + " / " + response.RecommendedNextAction);
            string notice =
                "'PerFrameTraceNoticeFixture.Update' matches a per-frame Unity message name; if the resolved line executes unconditionally, the marker hits on the next frame, before the input or event you meant to observe arrives. Prefer a line that only executes when that event happens (inside its guarding if), or hold the input down with simulate-keyboard KeyDown before arming.";
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
