using System;
using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies edited-line remap onto a named method's compiled span, including the UseCase route.
    /// </summary>
    [TestFixture]
    public sealed class PausePointEditedLineRemapTests
    {
        private const string RemapFixtureFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/EditedLineRemapFixture.cs";
        private const string RoundForwardFixtureFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/EditedLineRemapRoundForwardFixture.cs";
        private const int UniqueTargetStatementLine = 10;
        private const int UniqueOtherStatementLine = 16;
        private const int DuplicateOtherStatementLine = 30;
        private const int ZeroMatchOtherStatementLine = 36;
        private const int CommentOtherCommentLine = 16;

        private const string ExpectedRemapWarning =
            "--line 16 did not resolve in method 'UniqueTarget' against the last compiled source; the edited line's text was found at line 10 inside that method's compiled span, so the marker was placed there. Verify ResolvedLocation, or run 'uloop compile' and re-enable to use edited-file line numbers.";

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
        /// What: a single trimmed match inside the named method span remaps to that compiled line.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenOneMatchInSpan_ReturnsThatLine()
        {
            IReadOnlyList<string> compiledSourceLines = new[]
            {
                "void Target()",
                "    int uniqueRemapProbe = value + 1;",
                "    return uniqueRemapProbe;",
                "}",
                "    int uniqueRemapProbe = value + 1;"
            };
            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans = new[]
            {
                new SourcePausePointCompiledMethodSpan(1, 4)
            };

            int remapped = PausePointEditedLineRemap.FindUniqueMatchingCompiledLineOrZero(
                "Target",
                "    int uniqueRemapProbe = value + 1;",
                compiledSourceLines,
                spans);

            Assert.That(remapped, Is.EqualTo(2));
        }

        /// <summary>
        /// What: a match that exists only outside the named method span does not remap.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenMatchIsOutsideSpan_ReturnsZero()
        {
            IReadOnlyList<string> compiledSourceLines = new[]
            {
                "void Target()",
                "    return value;",
                "}",
                "    int uniqueRemapProbe = value + 1;"
            };
            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans = new[]
            {
                new SourcePausePointCompiledMethodSpan(1, 3)
            };

            int remapped = PausePointEditedLineRemap.FindUniqueMatchingCompiledLineOrZero(
                "Target",
                "int uniqueRemapProbe = value + 1;",
                compiledSourceLines,
                spans);

            Assert.That(remapped, Is.EqualTo(0));
        }

        /// <summary>
        /// What: two matches inside the named method span do not remap.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenMultipleMatchesInSpan_ReturnsZero()
        {
            IReadOnlyList<string> compiledSourceLines = new[]
            {
                "void Target()",
                "    _ = 12345;",
                "    int skip = 0;",
                "    _ = 12345;",
                "}"
            };
            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans = new[]
            {
                new SourcePausePointCompiledMethodSpan(1, 5)
            };

            int remapped = PausePointEditedLineRemap.FindUniqueMatchingCompiledLineOrZero(
                "Target",
                "_ = 12345;",
                compiledSourceLines,
                spans);

            Assert.That(remapped, Is.EqualTo(0));
        }

        /// <summary>
        /// What: remap is skipped when --method is omitted even if the span has one match.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenMethodFilterIsEmpty_ReturnsZero()
        {
            IReadOnlyList<string> compiledSourceLines = new[]
            {
                "    int uniqueRemapProbe = value + 1;"
            };
            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans = new[]
            {
                new SourcePausePointCompiledMethodSpan(1, 1)
            };

            int remapped = PausePointEditedLineRemap.FindUniqueMatchingCompiledLineOrZero(
                string.Empty,
                "int uniqueRemapProbe = value + 1;",
                compiledSourceLines,
                spans);

            Assert.That(remapped, Is.EqualTo(0));
        }

        /// <summary>
        /// What: the remap warning is the planned fixed literal.
        /// </summary>
        [Test]
        public void BuildEditedLineRemapWarning_UsesFixedLiteral()
        {
            string warning = PausePointEnableWarnings.BuildEditedLineRemapWarning(16, "UniqueTarget", 10);

            Assert.That(warning, Is.EqualTo(ExpectedRemapWarning));
        }

        /// <summary>
        /// What: UseCase resolve failure remaps onto the unique compiled span line and patches there.
        /// </summary>
        [Test]
        public void Enable_WhenEditedLineMatchesOnceInNamedMethodSpan_RemapsAndPatches()
        {
            SourcePausePointResolveResult expected = SourcePausePointResolver.Resolve(
                RemapFixtureFile,
                UniqueTargetStatementLine,
                "UniqueTarget");
            Assert.That(expected.Success, Is.True, expected.ErrorMessage);

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = RemapFixtureFile,
                Line = UniqueOtherStatementLine,
                Method = "UniqueTarget",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLine, Is.EqualTo(UniqueTargetStatementLine));
            Assert.That(response.ResolvedLineText, Is.EqualTo("int uniqueRemapProbe = value + 1;"));
            Assert.That(response.ResolvedMethod, Is.EqualTo(expected.Resolution.MethodDisplayName));
            Assert.That(
                response.Id,
                Is.EqualTo(RemapFixtureFile + ":" + UniqueOtherStatementLine));
            Assert.That(
                response.SnapshotTiming,
                Is.EqualTo(SourcePausePointConstants.PreLineSnapshotTimingNote));
            string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.CreateEnableWarning(),
                    ExpectedRemapWarning),
                SourcePausePointConstants.SmallMethodInliningRiskWarning);
            Assert.That(response.Warning, Is.EqualTo(expectedWarning));
        }

        /// <summary>
        /// What: zero matches inside the named method span leave the existing resolve failure unchanged.
        /// </summary>
        [Test]
        public void Enable_WhenEditedLineDoesNotMatchNamedMethodSpan_KeepsResolveFailure()
        {
            SourcePausePointResolveResult failed = SourcePausePointResolver.Resolve(
                RemapFixtureFile,
                ZeroMatchOtherStatementLine,
                "UniqueTarget");
            Assert.That(failed.Success, Is.False, failed.ErrorMessage);

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = RemapFixtureFile,
                Line = ZeroMatchOtherStatementLine,
                Method = "UniqueTarget",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            string expectedMessage = PausePointEnableWarnings.BuildResolveFailureMessage(
                failed.ErrorMessage,
                failed.NearbyCompiledMethods,
                hasActiveHotReloadPatches: false,
                ZeroMatchOtherStatementLine,
                requestedLineReadOk: false,
                requestedLineEditedText: string.Empty,
                compiledSourceLinesOrNull: null);
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
            Assert.That(response.ResolvedLine, Is.EqualTo(0));
            Assert.That(response.ResolvedMethod, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: multiple matches inside the named method span leave the existing resolve failure unchanged.
        /// </summary>
        [Test]
        public void Enable_WhenEditedLineMatchesTwiceInNamedMethodSpan_KeepsResolveFailure()
        {
            SourcePausePointResolveResult failed = SourcePausePointResolver.Resolve(
                RemapFixtureFile,
                DuplicateOtherStatementLine,
                "DuplicateTarget");
            Assert.That(failed.Success, Is.False, failed.ErrorMessage);

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = RemapFixtureFile,
                Line = DuplicateOtherStatementLine,
                Method = "DuplicateTarget",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            string expectedMessage = PausePointEnableWarnings.BuildResolveFailureMessage(
                failed.ErrorMessage,
                failed.NearbyCompiledMethods,
                hasActiveHotReloadPatches: false,
                DuplicateOtherStatementLine,
                requestedLineReadOk: false,
                requestedLineEditedText: string.Empty,
                compiledSourceLinesOrNull: null);
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
        }

        /// <summary>
        /// What: a unique span match that only rounds forward on re-resolve keeps the original failure.
        /// </summary>
        [Test]
        public void Enable_WhenRemappedLineRoundsForward_KeepsResolveFailure()
        {
            SourcePausePointResolveResult failed = SourcePausePointResolver.Resolve(
                RoundForwardFixtureFile,
                CommentOtherCommentLine,
                "CommentTarget");
            Assert.That(failed.Success, Is.False, failed.ErrorMessage);
            SourcePausePointResolveResult rounded = SourcePausePointResolver.Resolve(
                RoundForwardFixtureFile,
                9,
                "CommentTarget");
            Assert.That(rounded.Success, Is.True, rounded.ErrorMessage);
            Assert.That(rounded.Resolution.ResolvedLine, Is.Not.EqualTo(9));

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = RoundForwardFixtureFile,
                Line = CommentOtherCommentLine,
                Method = "CommentTarget",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            string expectedMessage = PausePointEnableWarnings.BuildResolveFailureMessage(
                failed.ErrorMessage,
                failed.NearbyCompiledMethods,
                hasActiveHotReloadPatches: false,
                CommentOtherCommentLine,
                requestedLineReadOk: false,
                requestedLineEditedText: string.Empty,
                compiledSourceLinesOrNull: null);
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
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
