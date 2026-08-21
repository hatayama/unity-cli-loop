using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.Tests.PausePointToolsFixtures;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies enable-pause-point arming next-action, re-arm discard warning, and closing-brace warning.
    /// </summary>
    [TestFixture]
    public sealed class PausePointEnableGuidanceTests
    {
        private const string FixtureFilePath = "Assets/Tests/Editor/PausePointToolsFixture.cs";
        private const int FixtureStatementLine = 12;
        private const int FixtureClosingBraceLine = 13;

        private const string ExpectedArmingNextActionForJump =
            "Run the code path so the marker can hit, then read the outcome with: uloop pause-point-status --id \"jump\". To arm, trigger, and collect in one call, add --await --resume-play --trigger \"<uloop command>\" next time.";

        private const string ExpectedRearmDiscardWarningGeneration1 =
            "Generation 1 of this pause point had already hit; this re-arm discarded its CapturedVariables and CapturedVariableHistory. Read results with pause-point-status before re-arming when you need them.";

        private const string ExpectedClosingBraceWarningForPlayerMoveLine42 =
            "--line resolved to the method's closing brace at line 42. Every return path through Player.Move reaches this line, including early returns, so captured variables can reflect a different path than the one you meant. To observe one specific path, target a statement line inside that path.";

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
        /// What: filling an empty success RecommendedNextAction uses the arming guidance literal.
        /// </summary>
        [Test]
        public void ResolveSuccessEnableRecommendedNextAction_WhenExistingIsEmpty_ReturnsArmingGuidance()
        {
            string action = PausePointEnableWarnings.ResolveSuccessEnableRecommendedNextAction(
                string.Empty,
                "jump");

            Assert.That(action, Is.EqualTo(ExpectedArmingNextActionForJump));
        }

        /// <summary>
        /// What: a non-empty RecommendedNextAction is kept verbatim and is not replaced by arming guidance.
        /// </summary>
        [Test]
        public void ResolveSuccessEnableRecommendedNextAction_WhenExistingIsNonEmpty_KeepsExisting()
        {
            const string existing =
                "Verify ResolvedLineText is the statement you intended. If it is not, run 'uloop compile' "
                + "and re-enable the pause point.";

            string action = PausePointEnableWarnings.ResolveSuccessEnableRecommendedNextAction(
                existing,
                "jump");

            Assert.That(action, Is.EqualTo(existing));
        }

        /// <summary>
        /// What: enable by id fills RecommendedNextAction with the arming guidance for that id.
        /// </summary>
        [Test]
        public void Enable_WhenIdPathSucceeds_SetsArmingRecommendedNextAction()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.RecommendedNextAction, Is.EqualTo(ExpectedArmingNextActionForJump));
        }

        /// <summary>
        /// What: enable by file:line fills RecommendedNextAction with arming guidance for the derived id.
        /// </summary>
        [Test]
        public void Enable_WhenFileLinePathSucceeds_SetsArmingRecommendedNextAction()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureFilePath,
                Line = FixtureStatementLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message);
            string expected =
                "Run the code path so the marker can hit, then read the outcome with: uloop pause-point-status --id \""
                + FixtureFilePath
                + ":"
                + FixtureStatementLine
                + "\". To arm, trigger, and collect in one call, add --await --resume-play --trigger \"<uloop command>\" next time.";
            Assert.That(response.RecommendedNextAction, Is.EqualTo(expected));
        }

        /// <summary>
        /// What: re-arming an id that already hit warns with the previous generation number.
        /// </summary>
        [Test]
        public void Enable_WhenReArmingAfterHit_AppendsDiscardWarning()
        {
            PausePointUseCase useCase = new PausePointUseCase();
            PausePointResponse first = useCase.Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
            Assert.That(first.Success, Is.True, first.ErrorCode + " / " + first.Message);
            Assert.That(first.Generation, Is.EqualTo(1));
            UloopPausePointRegistry.Hit("jump");

            PausePointResponse response = useCase.Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.CreateEnableWarning(),
                ExpectedRearmDiscardWarningGeneration1);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
        }

        /// <summary>
        /// What: re-arming an id that never hit does not add the capture-discard warning.
        /// </summary>
        [Test]
        public void Enable_WhenReArmingBeforeHit_OmitsDiscardWarning()
        {
            PausePointUseCase useCase = new PausePointUseCase();
            PausePointResponse first = useCase.Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
            Assert.That(first.Success, Is.True, first.ErrorCode + " / " + first.Message);

            PausePointResponse response = useCase.Enable(new EnablePausePointSchema
            {
                Id = "jump",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.Warning, Is.EqualTo(PausePointEnableWarnings.CreateEnableWarning()));
        }

        /// <summary>
        /// What: a resolved line whose trimmed text is the closing brace uses the closing-brace warning literal.
        /// </summary>
        [Test]
        public void BuildClosingBraceWarningOrEmpty_WhenResolvedLineIsClosingBrace_ReturnsWarning()
        {
            string warning = PausePointEnableWarnings.BuildClosingBraceWarningOrEmpty(
                "}",
                42,
                "Player.Move");

            Assert.That(warning, Is.EqualTo(ExpectedClosingBraceWarningForPlayerMoveLine42));
        }

        /// <summary>
        /// What: a non-brace resolved line does not produce the closing-brace warning.
        /// </summary>
        [Test]
        public void BuildClosingBraceWarningOrEmpty_WhenResolvedLineIsNotClosingBrace_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildClosingBraceWarningOrEmpty(
                "return sum;",
                12,
                "EnableBySourceLocationFixture.Add");

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: enabling on the fixture method's closing brace appends the ungated closing-brace warning.
        /// </summary>
        [Test]
        public void Enable_WhenResolvedLineIsMethodClosingBrace_AppendsClosingBraceWarning()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = FixtureFilePath,
                Line = FixtureClosingBraceLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(
                response.Success,
                Is.True,
                response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLineText, Is.EqualTo("}"));
            string expectedClosingBrace =
                "--line resolved to the method's closing brace at line "
                + response.ResolvedLine
                + ". Every return path through "
                + response.ResolvedMethod
                + " reaches this line, including early returns, so captured variables can reflect a different path than the one you meant. To observe one specific path, target a statement line inside that path.";
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.CreateEnableWarning(),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning),
                expectedClosingBrace);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
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
