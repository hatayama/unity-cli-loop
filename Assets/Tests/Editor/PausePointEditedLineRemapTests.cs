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
            "--line 16 in method 'UniqueTarget' was matched by its text to line 10 in the last compiled source, so the marker was placed at line 10, not at line 16. Verify ResolvedLocation, or run 'uloop compile' and re-enable to use edited-file line numbers.";

        private const string ExpectedSuccessWarning =
            "--line 16 in method 'UniqueTarget' was matched by its text to line 10 in the last compiled source, so the marker was placed at line 10, not at line 16. Verify ResolvedLocation, or run 'uloop compile' and re-enable to use edited-file line numbers. The target method body is very small and may be inlined by Mono's JIT into its callers; if HitCount stays 0 while the line demonstrably runs, move the pause point into the calling method.";

        private const string ExpectedZeroMatchFailureMessage =
            "No method named 'UniqueTarget' with a sequence point on or after line 36 was found. Nearby methods in the last compiled source: 'EditedLineRemapFixture.ZeroMatchOther' spans lines 35-38.";

        private const string ExpectedDuplicateMatchFailureMessage =
            "No method named 'DuplicateTarget' with a sequence point on or after line 30 was found. Nearby methods in the last compiled source: 'EditedLineRemapFixture.DuplicateOther' spans lines 29-32.";

        private const string ExpectedRoundForwardFailureMessage =
            "No method named 'CommentTarget' with a sequence point on or after line 16 was found. Nearby methods in the last compiled source: 'EditedLineRemapRoundForwardFixture.CommentOther' spans lines 15-18.";

        private const string ExpectedNoSnapshotFailureMessage =
            "No method named 'UniqueTarget' with a sequence point on or after line 16 was found. Nearby methods in the last compiled source: 'EditedLineRemapFixture.UniqueOther' spans lines 15-18.";

        private HotReloadSidePortScope _hotReloadSideScope;

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
            _hotReloadSideScope = new HotReloadSidePortScope();
            _hotReloadSideScope.Port.VerifiedSnapshotSourceForFile = _ => null;
        }

        [TearDown]
        public void TearDown()
        {
            _hotReloadSideScope.Dispose();
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
        /// What: a match on the declaration line just above the span remaps to the span's first
        /// line, which the retry can pin because it holds a sequence point.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenMatchIsOnDeclarationLineAboveSpan_ReturnsSpanStart()
        {
            int remapped = RemapIntoSpan(
                new[] { "", "    private bool Target(int value)", "    {", "        return value > 0;", "    }" },
                new SourcePausePointCompiledMethodSpan(3, 5),
                "private bool Target(int value)");

            Assert.That(remapped, Is.EqualTo(3));
        }

        /// <summary>
        /// What: a blank line ends the declaration lines, so a match above it does not remap.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenDeclarationLineMatchIsSeparatedByBlankLine_ReturnsZero()
        {
            int remapped = RemapIntoSpan(
                new[] { "    private bool Target(int value)", "", "    {", "        return value > 0;", "    }" },
                new SourcePausePointCompiledMethodSpan(3, 5),
                "private bool Target(int value)");

            Assert.That(remapped, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a previous method's closing brace ends the declaration lines, so a match above it
        /// does not remap.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenDeclarationLineMatchIsBeyondPreviousMethodEnd_ReturnsZero()
        {
            int remapped = RemapIntoSpan(
                new[] { "    private bool Target(int value)", "    }", "    {", "        return value > 0;", "    }" },
                new SourcePausePointCompiledMethodSpan(3, 5),
                "private bool Target(int value)");

            Assert.That(remapped, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a declaration-line match and a span-line match of the same text count as two hits
        /// and do not remap.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenDeclarationLineAndSpanLineBothMatch_ReturnsZero()
        {
            int remapped = RemapIntoSpan(
                new[] { "    void Target(int value)", "    {", "        void Target(int value)", "    }" },
                new SourcePausePointCompiledMethodSpan(2, 4),
                "void Target(int value)");

            Assert.That(remapped, Is.EqualTo(0));
        }

        /// <summary>
        /// What: a declaration line exactly as far above the span as the lookback reaches (six
        /// lines) still remaps to the span's first line.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenDeclarationLineIsAtLookbackLimit_ReturnsSpanStart()
        {
            int remapped = RemapIntoSpan(
                new[]
                {
                    "    private bool Target(int value)",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    {",
                    "        return value > 0;",
                    "    }"
                },
                new SourcePausePointCompiledMethodSpan(7, 9),
                "private bool Target(int value)");

            Assert.That(remapped, Is.EqualTo(7));
        }

        /// <summary>
        /// What: a match further above the span than the declaration lookback reaches does not remap.
        /// </summary>
        [Test]
        public void FindUniqueMatchingCompiledLine_WhenDeclarationLineIsBeyondLookbackLimit_ReturnsZero()
        {
            int remapped = RemapIntoSpan(
                new[]
                {
                    "    private bool Target(int value)",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    [SerializeField]",
                    "    {",
                    "        return value > 0;",
                    "    }"
                },
                new SourcePausePointCompiledMethodSpan(8, 10),
                "private bool Target(int value)");

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

        private static int RemapIntoSpan(
            IReadOnlyList<string> compiledSourceLines,
            SourcePausePointCompiledMethodSpan span,
            string editedLineText)
        {
            return PausePointEditedLineRemap.FindUniqueMatchingCompiledLineOrZero(
                "Target",
                editedLineText,
                compiledSourceLines,
                new[] { span });
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
        /// What: an edited line outside the named method remaps onto its unique compiled span line and patches there.
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

        /// <summary>
        /// What: a line whose text sits at the same line but outside the --method region is still
        /// matched by text instead of snapping to that method's entry brace, and the snap
        /// disclosure is not added on top of the remap warning. This also pins the no-drift
        /// exception: same text at the same line counts as no drift only inside the method region.
        /// </summary>
        [Test]
        public void Enable_WhenLineAboveNamedMethodMatchesItsBodyText_RemapsInsteadOfSnappingToEntry()
        {
            InstallSnapshotFromFile(RemapFixtureFile);
            _hotReloadSideScope.Port.ActiveHotReloadChangesInFile = _ => true;

            PausePointResponse response = EnableRemapFixture(UniqueTargetStatementLine, "UniqueOther");

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLine, Is.EqualTo(UniqueOtherStatementLine));
            Assert.That(
                response.Warning,
                Does.Contain("--line 10 in method 'UniqueOther' was matched by its text to line 16"));
            Assert.That(response.Warning, Does.Not.Contain("snapped forward"));
        }

        /// <summary>
        /// What: a line inside the named method whose statement moved in the compiled source is
        /// matched by text instead of resolving to the other statement now at that line number.
        /// </summary>
        [Test]
        public void Enable_WhenStatementMovedInsideNamedMethod_RemapsToItsCompiledLine()
        {
            InstallSnapshotWithLinesSwapped(RemapFixtureFile, 10, 11);

            PausePointResponse response = EnableRemapFixture(11, "UniqueTarget");

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLine, Is.EqualTo(10));
            Assert.That(
                response.Warning,
                Does.Contain("--line 11 in method 'UniqueTarget' was matched by its text to line 10"));
        }

        /// <summary>
        /// What: a brace-only edited line is not text-matched and keeps the plain forward resolve.
        /// </summary>
        [Test]
        public void Enable_WhenEditedLineIsBraceOnly_KeepsPlainResolve()
        {
            InstallSnapshotFromFile(RemapFixtureFile);

            PausePointResponse response = EnableRemapFixture(9, "UniqueOther");

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLine, Is.EqualTo(15));
            Assert.That(response.Warning, Does.Not.Contain("was matched by its text"));
        }

        /// <summary>
        /// What: a statement line that has not drifted resolves in place without a remap warning.
        /// </summary>
        [Test]
        public void Enable_WhenStatementLineHasNotDrifted_ResolvesInPlaceWithoutRemapWarning()
        {
            InstallSnapshotFromFile(RemapFixtureFile);

            PausePointResponse response = EnableRemapFixture(UniqueTargetStatementLine, "UniqueTarget");

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLine, Is.EqualTo(UniqueTargetStatementLine));
            Assert.That(response.Warning, Does.Not.Contain("was matched by its text"));
        }

        /// <summary>
        /// What: a declaration line that has not drifted keeps the plain round-forward to the entry
        /// brace without a remap warning, even though its text match reports the span start.
        /// </summary>
        [Test]
        public void Enable_WhenDeclarationLineHasNotDrifted_RoundsForwardWithoutRemapWarning()
        {
            InstallSnapshotFromFile(RemapFixtureFile);

            PausePointResponse response = EnableRemapFixture(8, "UniqueTarget");

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLine, Is.EqualTo(9));
            Assert.That(response.Warning, Does.Not.Contain("was matched by its text"));
        }

        /// <summary>
        /// What: when the plain resolve fails, a brace-only edited line is still text-matched inside
        /// the named method, as before the text-first match, so the enable keeps succeeding.
        /// </summary>
        [Test]
        public void Enable_WhenPlainResolveFailsOnBraceOnlyLine_StillRemapsInsideNamedMethod()
        {
            InstallSnapshotFromFile(RemapFixtureFile);

            PausePointResponse response = EnableRemapFixture(15, "UniqueTarget");

            Assert.That(response.Success, Is.True, response.ErrorCode + " / " + response.Message);
            Assert.That(response.ResolvedLine, Is.EqualTo(9));
            Assert.That(
                response.Warning,
                Does.Contain("--line 15 in method 'UniqueTarget' was matched by its text to line 9"));
        }

        private static PausePointResponse EnableRemapFixture(int line, string method)
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = RemapFixtureFile,
                Line = line,
                Method = method,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
        }

        private void InstallSnapshotWithLinesSwapped(string projectRelativeFile, int lineA, int lineB)
        {
            string absoluteFilePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                projectRelativeFile);
            string[] lines = File.ReadAllText(absoluteFilePath).Split('\n');
            string swapped = lines[lineA - 1];
            lines[lineA - 1] = lines[lineB - 1];
            lines[lineB - 1] = swapped;
            string snapshotSource = string.Join("\n", lines);
            _hotReloadSideScope.Port.VerifiedSnapshotSourceForFile = _ => snapshotSource;
        }

        private void InstallSnapshotFromFile(string projectRelativeFile)
        {
            string absoluteFilePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                projectRelativeFile);
            string snapshotSource = File.ReadAllText(absoluteFilePath);
            _hotReloadSideScope.Port.VerifiedSnapshotSourceForFile = _ => snapshotSource;
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
