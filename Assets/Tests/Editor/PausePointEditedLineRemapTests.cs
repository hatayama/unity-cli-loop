using System;
using System.Collections.Generic;
using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

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

        private const string ExpectedUniqueTargetResolvedMethod =
            "System.Int32 io.github.hatayama.UnityCliLoop.Tests.SourcePausePointResolverFixtures.EditedLineRemapFixture::UniqueTarget(System.Int32)";

        private const string ExpectedRemapWarning =
            "--line 16 did not resolve in method 'UniqueTarget' against the last compiled source; the edited line's text was found at line 10 inside that method's compiled span, so the marker was placed there. Verify ResolvedLocation, or run 'uloop compile' and re-enable to use edited-file line numbers.";

        private const string ExpectedSuccessWarning =
            "--line 16 did not resolve in method 'UniqueTarget' against the last compiled source; the edited line's text was found at line 10 inside that method's compiled span, so the marker was placed there. Verify ResolvedLocation, or run 'uloop compile' and re-enable to use edited-file line numbers. The target method body is very small and may be inlined by Mono's JIT into its callers; if HitCount stays 0 while the line demonstrably runs, move the pause point into the calling method.";

        private const string ExpectedZeroMatchFailureMessage =
            "No method named 'UniqueTarget' with a sequence point on or after line 36 was found. Nearby methods in the last compiled source: 'EditedLineRemapFixture.ZeroMatchOther' spans lines 35-38.";

        private const string ExpectedDuplicateMatchFailureMessage =
            "No method named 'DuplicateTarget' with a sequence point on or after line 30 was found. Nearby methods in the last compiled source: 'EditedLineRemapFixture.DuplicateOther' spans lines 29-32.";

        private const string ExpectedRoundForwardFailureMessage =
            "No method named 'CommentTarget' with a sequence point on or after line 16 was found. Nearby methods in the last compiled source: 'EditedLineRemapRoundForwardFixture.CommentOther' spans lines 15-18.";

        private const string ExpectedNoSnapshotFailureMessage =
            "No method named 'UniqueTarget' with a sequence point on or after line 16 was found. Nearby methods in the last compiled source: 'EditedLineRemapFixture.UniqueOther' spans lines 15-18.";

        private Func<string, string> _previousSnapshotLoader;

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
            _previousSnapshotLoader = HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile;
            HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = null;
        }

        [TearDown]
        public void TearDown()
        {
            HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = _previousSnapshotLoader;
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
        /// What: overlapping spans that share one matching line count as two hits and do not remap.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenOverlappingSpansShareTheMatch_ReturnsZero()
        {
            IReadOnlyList<string> compiledSourceLines = new[]
            {
                "void Foo()",
                "    int sharedRemapProbe = 1;",
                "    return sharedRemapProbe;",
                "}"
            };
            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans = new[]
            {
                new SourcePausePointCompiledMethodSpan(1, 4),
                new SourcePausePointCompiledMethodSpan(2, 4)
            };

            int remapped = PausePointEditedLineRemap.FindUniqueMatchingCompiledLineOrZero(
                "Foo",
                "int sharedRemapProbe = 1;",
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
            InstallSnapshotFromFile(RemapFixtureFile);

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
            Assert.That(response.ResolvedMethod, Is.EqualTo(ExpectedUniqueTargetResolvedMethod));
            Assert.That(
                response.Id,
                Is.EqualTo(RemapFixtureFile + ":" + UniqueOtherStatementLine));
            Assert.That(
                response.SnapshotTiming,
                Is.EqualTo(SourcePausePointConstants.PreLineSnapshotTimingNote));
            Assert.That(response.Warning, Is.EqualTo(ExpectedSuccessWarning));
        }

        /// <summary>
        /// What: without a verified compiled snapshot the UseCase keeps the existing resolve failure.
        /// </summary>
        [Test]
        public void Enable_WhenVerifiedSnapshotIsMissing_KeepsResolveFailure()
        {
            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = RemapFixtureFile,
                Line = UniqueOtherStatementLine,
                Method = "UniqueTarget",
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(response.Message, Is.EqualTo(ExpectedNoSnapshotFailureMessage));
            Assert.That(response.ResolvedLine, Is.EqualTo(0));
            Assert.That(response.ResolvedMethod, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: zero matches inside the named method span leave the existing resolve failure unchanged.
        /// </summary>
        [Test]
        public void Enable_WhenEditedLineDoesNotMatchNamedMethodSpan_KeepsResolveFailure()
        {
            InstallSnapshotFromFile(RemapFixtureFile);

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
            Assert.That(response.Message, Is.EqualTo(ExpectedZeroMatchFailureMessage));
            Assert.That(response.ResolvedLine, Is.EqualTo(0));
            Assert.That(response.ResolvedMethod, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: multiple matches inside the named method span leave the existing resolve failure unchanged.
        /// </summary>
        [Test]
        public void Enable_WhenEditedLineMatchesTwiceInNamedMethodSpan_KeepsResolveFailure()
        {
            InstallSnapshotFromFile(RemapFixtureFile);

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
            Assert.That(response.Message, Is.EqualTo(ExpectedDuplicateMatchFailureMessage));
        }

        /// <summary>
        /// What: a unique span match that only rounds forward on re-resolve keeps the original failure.
        /// </summary>
        [Test]
        public void Enable_WhenRemappedLineRoundsForward_KeepsResolveFailure()
        {
            InstallSnapshotFromFile(RoundForwardFixtureFile);

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
            Assert.That(response.Message, Is.EqualTo(ExpectedRoundForwardFailureMessage));
        }

        private static void InstallSnapshotFromFile(string projectRelativeFile)
        {
            string absoluteFilePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                projectRelativeFile);
            string snapshotSource = File.ReadAllText(absoluteFilePath);
            HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = _ => snapshotSource;
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
