using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies the compiled-line-map warning text for files that have active hot-reload patches.
    /// </summary>
    [TestFixture]
    public sealed class PausePointCompiledLineMapWarningTests
    {
        private const string ForwardSlashFile = "Assets/Scripts/Example.cs";

        private const string ExampleResolvedMethod = "ExampleType.ExampleMethod";

        private const string ExpectedCompiledLineMapWarning =
            "--line resolved against the last compiled source, not the edited file: "
            + "'Assets/Scripts/Example.cs' has active hot-reload patches and the resolved method "
            + "'ExampleType.ExampleMethod' is not patched by this reload. Verify ResolvedLineText "
            + "matches the statement you meant, or run 'uloop compile' and re-enable.";

        private const string ExpectedCompiledLineMapMatchedWarning =
            "No drift is visible at this line: the statement text at the resolved line is "
            + "identical in the edited file. 'Assets/Scripts/Example.cs' has active hot-reload "
            + "patches and the resolved method 'ExampleType.ExampleMethod' is not patched by "
            + "this reload, so --line resolved against the last compiled source, not the edited file.";

        private const string ExpectedCompiledLineMapResolveFailureWarning =
            "'Assets/Scripts/Example.cs' has active hot-reload patches. --line resolves against "
            + "the last compiled source, not the edited file, so a line number taken from the "
            + "edited file can miss or fail to resolve. Methods currently patched by hot reload "
            + "resolve against the edited file instead. Recompute the line against the last "
            + "compiled source, or run 'uloop compile' and re-enable.";

        private const string ExpectedEnableResolveFailureWarning =
            "'Assets/Tests/Editor/PausePointCompiledLineMapWarningTests.cs' has active "
            + "hot-reload patches. --line resolves against the last compiled source, not the "
            + "edited file, so a line number taken from the edited file can miss or fail to "
            + "resolve. Methods currently patched by hot reload resolve against the edited file "
            + "instead. Recompute the line against the last compiled source, or run 'uloop compile' "
            + "and re-enable.";

        private const string ResolveFailureFile =
            "Assets/Tests/Editor/PausePointCompiledLineMapWarningTests.cs";

        private const int UnresolvableLine = 999999;

        private const string GenericPatchedMethodsUseEditedFileSentence =
            "Methods currently patched by hot reload resolve against the edited file instead";

        private const string CompiledMethodSpanFixtureFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/CompiledMethodSpanFixture.cs";

        private const int CompiledMethodSpanFixtureBlankLine = 12;

        private const string MissingEditedLineFile =
            "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/DoesNotExistEditedLineRead.cs";

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
        /// What: an active-patch file produces the success-path compiled-line-map warning
        /// that names the unpatched resolved method.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenPatchesAreActive_ReturnsFormattedWarning()
        {
            string warning = PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(
                true,
                ForwardSlashFile,
                ExampleResolvedMethod,
                false);

            Assert.That(warning, Is.EqualTo(ExpectedCompiledLineMapWarning));
        }

        /// <summary>
        /// What: a backslash path is normalized before it is interpolated into the success warning.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenFileUsesBackslashes_NormalizesToForwardSlashes()
        {
            string warning = PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(
                true,
                "Assets\\Scripts\\Example.cs",
                ExampleResolvedMethod,
                false);

            Assert.That(warning, Is.EqualTo(ExpectedCompiledLineMapWarning));
        }

        /// <summary>
        /// What: the success helper stays silent when the file has no active hot-reload patches.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenPatchesAreInactive_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(
                false,
                ForwardSlashFile,
                ExampleResolvedMethod,
                false);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: when compiled and edited statement text already match, the success warning
        /// says so instead of asking the agent to verify ResolvedLineText by hand.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapWarningOrEmpty_WhenComparedAndMatched_ReturnsMatchedWarning()
        {
            string warning = PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(
                true,
                ForwardSlashFile,
                ExampleResolvedMethod,
                true);

            Assert.That(warning, Is.EqualTo(ExpectedCompiledLineMapMatchedWarning));
        }

        /// <summary>
        /// What: resolve-failure warning names compiled-line drift without pointing at
        /// ResolvedMethod or ResolvedLineText, which stay empty on that failure.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapResolveFailureWarningOrEmpty_WhenPatchesAreActive_ReturnsFormattedWarning()
        {
            string warning = PausePointEnableWarnings.BuildCompiledLineMapResolveFailureWarningOrEmpty(
                true,
                ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(ExpectedCompiledLineMapResolveFailureWarning));
        }

        /// <summary>
        /// What: a backslash path is normalized before it is interpolated into the
        /// resolve-failure warning.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapResolveFailureWarningOrEmpty_WhenFileUsesBackslashes_NormalizesToForwardSlashes()
        {
            string warning = PausePointEnableWarnings.BuildCompiledLineMapResolveFailureWarningOrEmpty(
                true,
                "Assets\\Scripts\\Example.cs");

            Assert.That(warning, Is.EqualTo(ExpectedCompiledLineMapResolveFailureWarning));
        }

        /// <summary>
        /// What: the resolve-failure helper stays silent when the file has no active patches.
        /// </summary>
        [Test]
        public void BuildCompiledLineMapResolveFailureWarningOrEmpty_WhenPatchesAreInactive_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildCompiledLineMapResolveFailureWarningOrEmpty(
                false,
                ForwardSlashFile);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: trim-unequal compiled vs edited text at the same resolved line formats the
        /// drift warning from the constant.
        /// </summary>
        [Test]
        public void BuildCompiledLineDriftWarningOrEmpty_WhenTextsDiffer_ReturnsFormattedWarning()
        {
            (string warning, bool comparedAndMatched) =
                PausePointCompiledLineComparisonWarnings.BuildCompiledLineDriftWarningOrEmpty(
                    "  return 1;  ",
                    "return 2;",
                    ForwardSlashFile,
                    17,
                    true);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                        ForwardSlashFile,
                        17,
                        "return 1;",
                        "return 2;")));
            Assert.That(comparedAndMatched, Is.False);
        }

        /// <summary>
        /// What: trim-equal compiled vs edited text is not drift.
        /// </summary>
        [Test]
        public void BuildCompiledLineDriftWarningOrEmpty_WhenTextsMatchAfterTrim_ReturnsEmpty()
        {
            (string warning, bool comparedAndMatched) =
                PausePointCompiledLineComparisonWarnings.BuildCompiledLineDriftWarningOrEmpty(
                    "  return 1;  ",
                    "return 1;",
                    ForwardSlashFile,
                    17,
                    true);

            Assert.That(warning, Is.EqualTo(string.Empty));
            Assert.That(comparedAndMatched, Is.True);
        }

        /// <summary>
        /// What: a missing compiled line, or a failed edited-line read, skips the comparison.
        /// </summary>
        [Test]
        public void BuildCompiledLineDriftWarningOrEmpty_WhenCompiledMissingOrEditedReadFails_ReturnsEmpty()
        {
            (string missingCompiledWarning, bool missingCompiledMatched) =
                PausePointCompiledLineComparisonWarnings.BuildCompiledLineDriftWarningOrEmpty(
                    string.Empty,
                    "return 1;",
                    ForwardSlashFile,
                    17,
                    true);
            Assert.That(missingCompiledWarning, Is.EqualTo(string.Empty));
            Assert.That(missingCompiledMatched, Is.False);

            (string readFailedWarning, bool readFailedMatched) =
                PausePointCompiledLineComparisonWarnings.BuildCompiledLineDriftWarningOrEmpty(
                    "return 1;",
                    string.Empty,
                    ForwardSlashFile,
                    17,
                    false);
            Assert.That(readFailedWarning, Is.EqualTo(string.Empty));
            Assert.That(readFailedMatched, Is.False);
        }

        /// <summary>
        /// What: a successfully read blank edited line at the resolved line is drift, not silence.
        /// </summary>
        [Test]
        public void BuildCompiledLineDriftWarningOrEmpty_WhenEditedLineIsBlankAndReadSucceeded_ReturnsBlankDriftWarning()
        {
            (string warning, bool comparedAndMatched) =
                PausePointCompiledLineComparisonWarnings.BuildCompiledLineDriftWarningOrEmpty(
                    "  {  ",
                    "   ",
                    ForwardSlashFile,
                    109,
                    true);

            Assert.That(
                warning,
                Is.EqualTo(
                    "'Assets/Scripts/Example.cs' line 109 is '{' in the last compiled source but blank in the edited file. "
                    + "The marker is armed on the compiled statement. If that is not the statement you meant, "
                    + "recompute --line against the last compiled source, or run 'uloop compile' and re-enable."));
            Assert.That(comparedAndMatched, Is.False);
        }

        /// <summary>
        /// What: a blank line that exists in an on-disk fixture is a successful read of empty text.
        /// </summary>
        [Test]
        public void ReadEditedLineText_WhenLineIsBlank_ReturnsReadOkWithEmptyText()
        {
            (bool readOk, string text) = PausePointCompiledLineComparisonWarnings.ReadEditedLineText(
                CompiledMethodSpanFixtureFile,
                CompiledMethodSpanFixtureBlankLine);

            Assert.That(readOk, Is.True);
            Assert.That(text, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a line number past the end of an on-disk fixture is a failed read, not a blank line.
        /// </summary>
        [Test]
        public void ReadEditedLineText_WhenLineIsPastEndOfFile_ReturnsReadFailed()
        {
            (bool readOk, string text) = PausePointCompiledLineComparisonWarnings.ReadEditedLineText(
                CompiledMethodSpanFixtureFile,
                UnresolvableLine);

            Assert.That(readOk, Is.False);
            Assert.That(text, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a missing file is a failed read, not a blank line.
        /// </summary>
        [Test]
        public void ReadEditedLineText_WhenFileDoesNotExist_ReturnsReadFailed()
        {
            (bool readOk, string text) = PausePointCompiledLineComparisonWarnings.ReadEditedLineText(
                MissingEditedLineFile,
                1);

            Assert.That(readOk, Is.False);
            Assert.That(text, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a forward snap from the requested line names the requested edited text and the armed method.
        /// </summary>
        [Test]
        public void BuildLineSnapDisclosureWarningOrEmpty_WhenResolvedLineDiffers_ReturnsSnapDisclosure()
        {
            string warning = PausePointCompiledLineComparisonWarnings.BuildLineSnapDisclosureWarningOrEmpty(
                ForwardSlashFile,
                107,
                109,
                "GameDirector.ComputeScoreTarget",
                true,
                "  LastRemainingBlocks = remainingBlocks;  ");

            Assert.That(
                warning,
                Is.EqualTo(
                    "'Assets/Scripts/Example.cs' --line 107 is 'LastRemainingBlocks = remainingBlocks;' in the edited file, "
                    + "but the marker snapped forward to line 109 in 'GameDirector.ComputeScoreTarget'."));
        }

        /// <summary>
        /// What: snap disclosure stays silent when the marker did not leave the requested line.
        /// </summary>
        [Test]
        public void BuildLineSnapDisclosureWarningOrEmpty_WhenResolvedLineEqualsRequestedLine_ReturnsEmpty()
        {
            string warning = PausePointCompiledLineComparisonWarnings.BuildLineSnapDisclosureWarningOrEmpty(
                ForwardSlashFile,
                109,
                109,
                "GameDirector.ComputeScoreTarget",
                true,
                "LastRemainingBlocks = remainingBlocks;");

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: a blank requested line still discloses the snap without quoting empty text.
        /// </summary>
        [Test]
        public void BuildLineSnapDisclosureWarningOrEmpty_WhenRequestedLineIsBlank_ReturnsBlankSnapDisclosure()
        {
            string warning = PausePointCompiledLineComparisonWarnings.BuildLineSnapDisclosureWarningOrEmpty(
                ForwardSlashFile,
                107,
                109,
                "GameDirector.ComputeScoreTarget",
                true,
                "   ");

            Assert.That(
                warning,
                Is.EqualTo(
                    "'Assets/Scripts/Example.cs' --line 107 is blank in the edited file, "
                    + "but the marker snapped forward to line 109 in 'GameDirector.ComputeScoreTarget'."));
        }

        /// <summary>
        /// What: a failed requested-line read discloses the snap without claiming the line was blank.
        /// </summary>
        [Test]
        public void BuildLineSnapDisclosureWarningOrEmpty_WhenRequestedLineReadFails_OmitsEditedText()
        {
            string warning = PausePointCompiledLineComparisonWarnings.BuildLineSnapDisclosureWarningOrEmpty(
                ForwardSlashFile,
                107,
                109,
                "GameDirector.ComputeScoreTarget",
                false,
                string.Empty);

            Assert.That(
                warning,
                Is.EqualTo(
                    "'Assets/Scripts/Example.cs' --line 107 snapped forward to line 109 in 'GameDirector.ComputeScoreTarget'."));
        }

        /// <summary>
        /// What: snap disclosure precedes resolved-line drift when both apply, using the existing
        /// non-blank drift sentence unchanged.
        /// </summary>
        [Test]
        public void ComposeCompiledLineDriftAndSnapWarningOrEmpty_WhenSnapAndNonBlankDrift_PutsSnapBeforeDrift()
        {
            (string warning, bool comparedAndMatched) = PausePointCompiledLineComparisonWarnings.ComposeCompiledLineDriftAndSnapWarningOrEmpty(
                ForwardSlashFile,
                10,
                17,
                "Example.Run",
                "return 1;",
                true,
                "return 3;",
                true,
                "return 2;",
                0,
                0,
                Array.Empty<string>());

            Assert.That(
                warning,
                Is.EqualTo(
                    "'Assets/Scripts/Example.cs' --line 10 is 'return 2;' in the edited file, "
                    + "but the marker snapped forward to line 17 in 'Example.Run'. "
                    + "'Assets/Scripts/Example.cs' line 17 is 'return 1;' in the last compiled source but 'return 3;' in the edited file. "
                    + "The marker is armed on the compiled statement. If that is not the statement you meant, "
                    + "recompute --line against the last compiled source, or run 'uloop compile' and re-enable."));
            Assert.That(comparedAndMatched, Is.False);
        }

        /// <summary>
        /// What: a forward snap onto a blank edited resolved line keeps a drift warning, discloses
        /// the snap, and lists the compiled line that matches the requested edited statement.
        /// </summary>
        [Test]
        public void ComposeCompiledLineDriftAndSnapWarningOrEmpty_WhenSnapAndBlankResolvedLine_DisclosesSnapBlankDriftAndRequestedCandidate()
        {
            string[] compiledSourceLines = new string[104];
            for (int index = 0; index < 103; index++)
            {
                compiledSourceLines[index] = "class Sample";
            }

            compiledSourceLines[103] = "            LastRemainingBlocks = remainingBlocks;";

            (string warning, bool comparedAndMatched) = PausePointCompiledLineComparisonWarnings.ComposeCompiledLineDriftAndSnapWarningOrEmpty(
                ForwardSlashFile,
                107,
                109,
                "GameDirector.ComputeScoreTarget",
                "{",
                true,
                string.Empty,
                true,
                "LastRemainingBlocks = remainingBlocks;",
                100,
                120,
                compiledSourceLines);

            Assert.That(
                warning,
                Is.EqualTo(
                    "'Assets/Scripts/Example.cs' --line 107 is 'LastRemainingBlocks = remainingBlocks;' in the edited file, "
                    + "but the marker snapped forward to line 109 in 'GameDirector.ComputeScoreTarget'. "
                    + "'Assets/Scripts/Example.cs' line 109 is '{' in the last compiled source but blank in the edited file. "
                    + "The marker is armed on the compiled statement. If that is not the statement you meant, "
                    + "recompute --line against the last compiled source, or run 'uloop compile' and re-enable. "
                    + "In the last compiled source, 'GameDirector.ComputeScoreTarget' spans lines 100-120. "
                    + "Candidate: the text at --line 107 in the edited file appears at line 104 in the last compiled source."));
            Assert.That(comparedAndMatched, Is.False);
        }

        /// <summary>
        /// What: a snap-only warning still lists a compiled match for the requested --line text
        /// and does not list the armed line as a candidate for its own text.
        /// </summary>
        [Test]
        public void ComposeCompiledLineDriftAndSnapWarningOrEmpty_WhenSnapOnly_OmitsResolvedSelfCandidate()
        {
            string[] compiledSourceLines =
            {
                "class Sample",
                "            {",
                "            LastRemainingBlocks = remainingBlocks;",
                "            {"
            };

            (string warning, bool comparedAndMatched) = PausePointCompiledLineComparisonWarnings.ComposeCompiledLineDriftAndSnapWarningOrEmpty(
                ForwardSlashFile,
                107,
                109,
                "GameDirector.ComputeScoreTarget",
                "{",
                true,
                "{",
                true,
                "LastRemainingBlocks = remainingBlocks;",
                100,
                120,
                compiledSourceLines);

            Assert.That(
                warning,
                Is.EqualTo(
                    "'Assets/Scripts/Example.cs' --line 107 is 'LastRemainingBlocks = remainingBlocks;' in the edited file, "
                    + "but the marker snapped forward to line 109 in 'GameDirector.ComputeScoreTarget'. "
                    + "In the last compiled source, 'GameDirector.ComputeScoreTarget' spans lines 100-120. "
                    + "Candidate: the text at --line 107 in the edited file appears at line 3 in the last compiled source."));
            Assert.That(comparedAndMatched, Is.True);
        }

        /// <summary>
        /// What: when resolved-line drift and a distinct requested-line match both exist, the
        /// two candidate sentences name different searches.
        /// </summary>
        [Test]
        public void ComposeCompiledLineDriftAndSnapWarningOrEmpty_WhenDriftAndRequestedLineBothMatch_DistinguishesCandidateSentences()
        {
            string[] compiledSourceLines =
            {
                "class Sample",
                "            return 3;",
                "            LastRemainingBlocks = remainingBlocks;"
            };

            (string warning, bool comparedAndMatched) = PausePointCompiledLineComparisonWarnings.ComposeCompiledLineDriftAndSnapWarningOrEmpty(
                ForwardSlashFile,
                107,
                109,
                "GameDirector.ComputeScoreTarget",
                "{",
                true,
                "return 3;",
                true,
                "LastRemainingBlocks = remainingBlocks;",
                0,
                0,
                compiledSourceLines);

            Assert.That(
                warning,
                Is.EqualTo(
                    "'Assets/Scripts/Example.cs' --line 107 is 'LastRemainingBlocks = remainingBlocks;' in the edited file, "
                    + "but the marker snapped forward to line 109 in 'GameDirector.ComputeScoreTarget'. "
                    + "'Assets/Scripts/Example.cs' line 109 is '{' in the last compiled source but 'return 3;' in the edited file. "
                    + "The marker is armed on the compiled statement. If that is not the statement you meant, "
                    + "recompute --line against the last compiled source, or run 'uloop compile' and re-enable. "
                    + "Candidate: the edited line's text appears at line 2 in the last compiled source. "
                    + "Candidate: the text at --line 107 in the edited file appears at line 3 in the last compiled source."));
            Assert.That(comparedAndMatched, Is.False);
        }

        /// <summary>
        /// What: enable on an unpatched method in a hot-reloaded file merges the exact drift
        /// warning and sets the drift next-action when compiled vs edited text differ.
        /// </summary>
        [Test]
        public void Enable_WhenCompiledLineDriftsFromEditedFile_AddsDriftWarningAndNextAction()
        {
            Func<string, HotReloadShimFileLookup> previousLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            Func<string, string> previousSnapshot =
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile;
            HotReloadShimFileLookup stubLookup = new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                null,
                Array.Empty<HotReloadShimMethodLookup>());

            string absolutePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                ResolveFailureFile);
            string diskSource = File.ReadAllText(absolutePath);
            int markerLine = FindLineNumberContaining(
                diskSource,
                "compiled-line-drift" + "-probe-unique");
            Assert.That(markerLine, Is.GreaterThan(0));
            int requestedLine = markerLine + 1;

            string[] snapshotLines = diskSource.Replace("\r\n", "\n").Split('\n');
            snapshotLines[requestedLine - 1] = "            return 0;";
            snapshotLines[markerLine - 1] = "            return 424242;";
            string snapshotSource = string.Join("\n", snapshotLines);

            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = _ => stubLookup;
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = _ => snapshotSource;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = ResolveFailureFile,
                    Line = requestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(
                    response.Success,
                    Is.True,
                    response.ErrorCode + " / " + response.Message + " / " + response.RecommendedNextAction);
                Assert.That(
                    response.ResolvedMethod,
                    Is.EqualTo(
                        "System.Int32 io.github.hatayama.UnityCliLoop.Tests.Editor.PausePointCompiledLineMapWarningTests::CompiledLineDriftProbe()"));
                SourcePausePointResolveResult spanResult = SourcePausePointResolver.Resolve(
                    ResolveFailureFile,
                    response.ResolvedLine);
                Assert.That(spanResult.Success, Is.True, spanResult.ErrorMessage);
                IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans =
                    SourcePausePointResolver.FindNamedCompiledMethodSpansInFile(ResolveFailureFile);
                Assert.That(spanResult.Resolution.CompiledMethodStartLine, Is.GreaterThan(0));
                Assert.That(spanResult.Resolution.CompiledMethodEndLine, Is.GreaterThan(0));
                string expectedDrift = string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                    ResolveFailureFile,
                    response.ResolvedLine,
                    "return 0;",
                    "return 424242;");
                expectedDrift = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                    expectedDrift,
                    response.ResolvedMethod,
                    spanResult.Resolution.CompiledMethodStartLine,
                    spanResult.Resolution.CompiledMethodEndLine);
                expectedDrift = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                    expectedDrift,
                    "return 424242;",
                    snapshotLines,
                    namedCompiledMethodSpans);
                string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.MergeWarnings(
                        PausePointEnableWarnings.MergeWarnings(
                            PausePointEnableWarnings.CreateEnableWarning(),
                            PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(
                                true,
                                ResolveFailureFile,
                                response.ResolvedMethod,
                                false)),
                        expectedDrift),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning);
                Assert.That(response.Warning, Is.EqualTo(expectedWarning));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.HotReloadCompiledLineMapLineDriftNextAction));
                AssertLineBasis(response, "LastCompiledSource");
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previousLookup;
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = previousSnapshot;
            }
        }

        /// <summary>
        /// What: enable on an unpatched method whose compiled and edited statement text match
        /// uses the matched compiled-line-map warning through PausePointUseCase.Enable.
        /// </summary>
        [Test]
        public void Enable_WhenCompiledLineMatchesEditedFile_UsesMatchedCompiledLineMapWarning()
        {
            Func<string, HotReloadShimFileLookup> previousLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            Func<string, string> previousSnapshot =
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile;
            HotReloadShimFileLookup stubLookup = new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                null,
                Array.Empty<HotReloadShimMethodLookup>());

            string absolutePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                ResolveFailureFile);
            string diskSource = File.ReadAllText(absolutePath);
            int markerLine = FindLineNumberContaining(
                diskSource,
                "compiled-line-drift" + "-probe-unique");
            Assert.That(markerLine, Is.GreaterThan(0));
            int requestedLine = markerLine + 1;

            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = _ => stubLookup;
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = _ => diskSource;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = ResolveFailureFile,
                    Line = requestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(
                    response.Success,
                    Is.True,
                    response.ErrorCode + " / " + response.Message + " / " + response.RecommendedNextAction);
                Assert.That(
                    response.ResolvedMethod,
                    Is.EqualTo(
                        "System.Int32 io.github.hatayama.UnityCliLoop.Tests.Editor.PausePointCompiledLineMapWarningTests::CompiledLineDriftProbe()"));
                string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.MergeWarnings(
                        PausePointEnableWarnings.CreateEnableWarning(),
                        PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(
                            true,
                            ResolveFailureFile,
                            response.ResolvedMethod,
                            true)),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning);
                Assert.That(response.Warning, Is.EqualTo(expectedWarning));
                string expectedArming =
                    "Run the code path so the marker can hit, then read the outcome with: uloop pause-point-status --id \""
                    + ResolveFailureFile
                    + ":"
                    + requestedLine
                    + "\". To arm, trigger, and collect in one call, add --await --resume-play --trigger \"<uloop command>\" next time.";
                Assert.That(response.RecommendedNextAction, Is.EqualTo(expectedArming));
                AssertLineBasis(response, "LastCompiledSource");
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previousLookup;
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = previousSnapshot;
            }
        }

        /// <summary>
        /// What: enable on a comment line that rounds forward discloses the snap even when the
        /// armed compiled and edited texts match, and still sets the drift next-action.
        /// </summary>
        [Test]
        public void Enable_WhenRequestedLineSnapsForward_DisclosesSnapAndSetsNextAction()
        {
            Func<string, HotReloadShimFileLookup> previousLookup =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            Func<string, string> previousSnapshot =
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile;
            HotReloadShimFileLookup stubLookup = new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                null,
                Array.Empty<HotReloadShimMethodLookup>());

            string absolutePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                ResolveFailureFile);
            string diskSource = File.ReadAllText(absolutePath);
            int requestedLine = FindLineNumberContaining(
                diskSource,
                "compiled-line-drift" + "-probe-unique");
            Assert.That(requestedLine, Is.GreaterThan(0));
            int compiledResolvedLine = requestedLine + 1;

            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = _ => stubLookup;
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = _ => diskSource;

                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = ResolveFailureFile,
                    Line = requestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(
                    response.Success,
                    Is.True,
                    response.ErrorCode + " / " + response.Message + " / " + response.RecommendedNextAction);
                Assert.That(
                    response.ResolvedMethod,
                    Is.EqualTo(
                        "System.Int32 io.github.hatayama.UnityCliLoop.Tests.Editor.PausePointCompiledLineMapWarningTests::CompiledLineDriftProbe()"));
                Assert.That(response.ResolvedLine, Is.EqualTo(compiledResolvedLine));
                SourcePausePointResolveResult spanResult = SourcePausePointResolver.Resolve(
                    ResolveFailureFile,
                    response.ResolvedLine);
                Assert.That(spanResult.Success, Is.True, spanResult.ErrorMessage);
                string requestedEditedText = "// compiled-line-drift" + "-probe-unique";
                string expectedSnap =
                    "'" + ResolveFailureFile + "' --line " + requestedLine
                    + " is '" + requestedEditedText + "' in the edited file, but the marker snapped forward to line "
                    + compiledResolvedLine + " in '" + response.ResolvedMethod + "'."
                    + " In the last compiled source, '" + response.ResolvedMethod + "' spans lines "
                    + spanResult.Resolution.CompiledMethodStartLine + "-"
                    + spanResult.Resolution.CompiledMethodEndLine + "."
                    + " Candidate: the text at --line " + requestedLine
                    + " in the edited file appears at line " + requestedLine
                    + " (in 'PausePointCompiledLineMapWarningTests.CompiledLineDriftProbe') in the last compiled source.";
                string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.MergeWarnings(
                        PausePointEnableWarnings.MergeWarnings(
                            PausePointEnableWarnings.CreateEnableWarning(),
                            PausePointEnableWarnings.BuildCompiledLineMapWarningOrEmpty(
                                true,
                                ResolveFailureFile,
                                response.ResolvedMethod,
                                true)),
                        expectedSnap),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning);
                Assert.That(response.Warning, Is.EqualTo(expectedWarning));
                Assert.That(
                    response.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.HotReloadCompiledLineMapLineDriftNextAction));
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previousLookup;
                HotReloadPausePointCoordination.GetVerifiedSnapshotSourceForFile = previousSnapshot;
            }
        }

        /// <summary>
        /// What: a known compiled span is appended to a non-empty drift warning.
        /// </summary>
        [Test]
        public void AppendCompiledMethodSpanToDriftWarningOrUnchanged_WhenSpanIsKnown_AppendsSpanSentence()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");

            string warning = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                drift,
                "Example.Run",
                8,
                11);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + string.Format(
                        SourcePausePointConstants.HotReloadCompiledMethodSpanInLastCompiledSourceFormat,
                        "Example.Run",
                        8,
                        11)));
        }

        /// <summary>
        /// What: an unknown (0,0) compiled span leaves the drift warning unchanged.
        /// </summary>
        [Test]
        public void AppendCompiledMethodSpanToDriftWarningOrUnchanged_WhenSpanIsUnknown_LeavesWarningUnchanged()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");

            string warning = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                drift,
                "Example.Run",
                0,
                0);

            Assert.That(warning, Is.EqualTo(drift));
        }

        /// <summary>
        /// What: an empty drift warning stays empty even when a compiled span is known.
        /// </summary>
        [Test]
        public void AppendCompiledMethodSpanToDriftWarningOrUnchanged_WhenDriftIsEmpty_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                string.Empty,
                "Example.Run",
                8,
                11);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: one trimmed compiled-source match appends a single-line candidate to the drift warning.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenOneLineMatches_AppendsSingleCandidate()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines =
            {
                "class Sample",
                "            return 2;",
                "            return 1;"
            };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "  return 2;  ",
                compiledLines);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + " Candidate: the edited line's text appears at line 2 in the last compiled source."));
        }

        /// <summary>
        /// What: a single resolved-line candidate inside a named compiled span identifies its method.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenSingleCandidateHasNamedSpan_AnnotatesMethod()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines =
            {
                "class Sample",
                "            return 2;",
                "            return 1;"
            };
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedSpans =
                new[]
                {
                    new SourcePausePointNearbyCompiledMethod("Enemy.TakeDamage", 2, 2)
                };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "return 2;",
                compiledLines,
                namedSpans);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + " Candidate: the edited line's text appears at line 2 (in 'Enemy.TakeDamage') in the last compiled source."));
        }

        /// <summary>
        /// What: overlapping compiled spans choose the smallest containing method for a candidate.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenNamedSpansOverlap_AnnotatesSmallestContainingMethod()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines = new string[12];
            compiledLines[11] = "return 2;";
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedSpans =
                new[]
                {
                    new SourcePausePointNearbyCompiledMethod("Enemy.Outer", 10, 20),
                    new SourcePausePointNearbyCompiledMethod("Enemy.Inner", 11, 13)
                };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "return 2;",
                compiledLines,
                namedSpans);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + " Candidate: the edited line's text appears at line 12 (in 'Enemy.Inner') in the last compiled source."));
        }

        /// <summary>
        /// What: candidate lines inside named compiled spans identify their containing methods.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenCandidatesHaveNamedSpans_AnnotatesEachMethod()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines =
            {
                "class Sample",
                "            return 2;",
                "            return 1;",
                "            return 2;"
            };
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedSpans =
                new[]
                {
                    new SourcePausePointNearbyCompiledMethod("Enemy.TakeDamage", 2, 2),
                    new SourcePausePointNearbyCompiledMethod("Enemy.Heal", 4, 4)
                };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "return 2;",
                compiledLines,
                namedSpans);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + " Candidate: the edited line's text appears at lines 2 (in 'Enemy.TakeDamage'), 4 (in 'Enemy.Heal') in the last compiled source."));
        }

        /// <summary>
        /// What: a mixed resolved-line candidate list leaves lines outside every named span bare.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenCandidatesMixSpansAndBareLine_AnnotatesOnlySpans()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines =
            {
                "            return 2;",
                "class Sample",
                "            return 2;",
                "class Other",
                "            return 2;"
            };
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedSpans =
                new[]
                {
                    new SourcePausePointNearbyCompiledMethod("Enemy.TakeDamage", 1, 1),
                    new SourcePausePointNearbyCompiledMethod("Enemy.Heal", 5, 5)
                };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "return 2;",
                compiledLines,
                namedSpans);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + " Candidate: the edited line's text appears at lines 1 (in 'Enemy.TakeDamage'), 3, 5 (in 'Enemy.Heal') in the last compiled source."));
        }

        /// <summary>
        /// What: two or three trimmed compiled-source matches append every matching line number.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenThreeLinesMatch_AppendsAllCandidates()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines =
            {
                "class Sample",
                "            return 2;",
                "            return 1;",
                "            return 2;",
                "            return 2;"
            };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "return 2;",
                compiledLines);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + " Candidate: the edited line's text appears at lines 2, 4, 5 in the last compiled source."));
        }

        /// <summary>
        /// What: more than three trimmed compiled-source matches append the first three and a truncation note.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenMoreThanThreeLinesMatch_CapsAtFirstThree()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines =
            {
                "            return 2;",
                "            return 1;",
                "            return 2;",
                "            return 2;",
                "            return 2;"
            };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "return 2;",
                compiledLines);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + " Candidate: the edited line's text appears at lines 1, 3, 4 (first 3 matches) in the last compiled source."));
        }

        /// <summary>
        /// What: annotations are rendered before the compiled-line candidate truncation suffix.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenAnnotatedCandidatesAreTruncated_AnnotatesBeforeSuffix()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines =
            {
                "            return 2;",
                "            return 1;",
                "            return 2;",
                "            return 2;",
                "            return 2;"
            };
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedSpans =
                new[]
                {
                    new SourcePausePointNearbyCompiledMethod("Enemy.TakeDamage", 1, 1),
                    new SourcePausePointNearbyCompiledMethod("Enemy.Heal", 3, 3),
                    new SourcePausePointNearbyCompiledMethod("Enemy.Revive", 4, 4)
                };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "return 2;",
                compiledLines,
                namedSpans);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift + " Candidate: the edited line's text appears at lines 1 (in 'Enemy.TakeDamage'), 3 (in 'Enemy.Heal'), 4 (in 'Enemy.Revive') (first 3 matches) in the last compiled source."));
        }

        /// <summary>
        /// What: no compiled-source match leaves the drift warning unchanged.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenNoLineMatches_LeavesWarningUnchanged()
        {
            string drift = string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                ForwardSlashFile,
                17,
                "return 1;",
                "return 2;");
            string[] compiledLines =
            {
                "class Sample",
                "            return 1;"
            };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                "return 2;",
                compiledLines);

            Assert.That(warning, Is.EqualTo(drift));
        }

        /// <summary>
        /// What: an empty drift warning stays empty even when compiled source contains the edited text.
        /// </summary>
        [Test]
        public void AppendCandidateCompiledLinesToDriftWarningOrUnchanged_WhenDriftIsEmpty_ReturnsEmpty()
        {
            string[] compiledLines =
            {
                "            return 2;"
            };

            string warning = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                string.Empty,
                "return 2;",
                compiledLines);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: one compiled-source match for the requested --line text names that --line.
        /// </summary>
        [Test]
        public void AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged_WhenOneLineMatches_NamesRequestedLine()
        {
            string drift =
                "'Assets/Scripts/Example.cs' --line 107 is 'return 2;' in the edited file, "
                + "but the marker snapped forward to line 109 in 'Example.Run'.";
            string[] compiledLines =
            {
                "class Sample",
                "            return 2;",
                "            return 1;"
            };

            string warning = PausePointEnableWarnings.AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                107,
                "  return 2;  ",
                compiledLines);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift
                    + " Candidate: the text at --line 107 in the edited file appears at line 2 in the last compiled source."));
        }

        /// <summary>
        /// What: multiple requested-line candidates inside named compiled spans identify each method.
        /// </summary>
        [Test]
        public void AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged_WhenMultipleCandidatesHaveNamedSpans_AnnotatesEachMethod()
        {
            string drift =
                "'Assets/Scripts/Example.cs' --line 107 is 'return 2;' in the edited file, "
                + "but the marker snapped forward to line 109 in 'Example.Run'.";
            string[] compiledLines =
            {
                "class Sample",
                "            return 2;",
                "            return 1;",
                "            return 2;"
            };
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedSpans =
                new[]
                {
                    new SourcePausePointNearbyCompiledMethod("Enemy.TakeDamage", 2, 2),
                    new SourcePausePointNearbyCompiledMethod("Enemy.Heal", 4, 4)
                };

            string warning = PausePointEnableWarnings.AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                107,
                "return 2;",
                compiledLines,
                namedSpans);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift
                    + " Candidate: the text at --line 107 in the edited file appears at lines 2 (in 'Enemy.TakeDamage'), 4 (in 'Enemy.Heal') in the last compiled source."));
        }

        /// <summary>
        /// What: more than three compiled-source matches for the requested --line text cap at the
        /// first three and name that --line.
        /// </summary>
        [Test]
        public void AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged_WhenMoreThanThreeLinesMatch_CapsAtFirstThree()
        {
            string drift =
                "'Assets/Scripts/Example.cs' --line 107 is 'return 2;' in the edited file, "
                + "but the marker snapped forward to line 109 in 'Example.Run'.";
            string[] compiledLines =
            {
                "            return 2;",
                "            return 1;",
                "            return 2;",
                "            return 2;",
                "            return 2;"
            };

            string warning = PausePointEnableWarnings.AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged(
                drift,
                107,
                "return 2;",
                compiledLines);

            Assert.That(
                warning,
                Is.EqualTo(
                    drift
                    + " Candidate: the text at --line 107 in the edited file appears at lines 1, 3, 4 (first 3 matches) in the last compiled source."));
        }

        /// <summary>
        /// What: one compiled-source match for the requested --line text is appended to a
        /// resolve-failure Message.
        /// </summary>
        [Test]
        public void AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged_WhenOneLineMatches_AppendsCandidate()
        {
            const string message =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            string[] compiledLines = new string[110];
            for (int index = 0; index < compiledLines.Length; index++)
            {
                compiledLines[index] = "            return 0;";
            }

            compiledLines[109] = "            return 2;";

            string result = PausePointEnableWarnings.AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                116,
                "  return 2;  ",
                compiledLines);

            Assert.That(
                result,
                Is.EqualTo(
                    "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'."
                    + " Candidate: the text at --line 116 in the edited file appears at line 110 in the last compiled source."));
        }

        /// <summary>
        /// What: two compiled-source matches for the requested --line text append both line numbers.
        /// </summary>
        [Test]
        public void AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged_WhenTwoLinesMatch_AppendsBothCandidates()
        {
            const string message =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            string[] compiledLines =
            {
                "class Sample",
                "            return 2;",
                "            return 1;",
                "            return 2;"
            };

            string result = PausePointEnableWarnings.AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                116,
                "return 2;",
                compiledLines);

            Assert.That(
                result,
                Is.EqualTo(
                    "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'."
                    + " Candidate: the text at --line 116 in the edited file appears at lines 2, 4 in the last compiled source."));
        }

        /// <summary>
        /// What: more than three compiled-source matches for a resolve-failure Message cap at the
        /// first three.
        /// </summary>
        [Test]
        public void AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged_WhenMoreThanThreeLinesMatch_CapsAtFirstThree()
        {
            const string message =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            string[] compiledLines =
            {
                "            return 2;",
                "            return 1;",
                "            return 2;",
                "            return 2;",
                "            return 2;"
            };

            string result = PausePointEnableWarnings.AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                116,
                "return 2;",
                compiledLines);

            Assert.That(
                result,
                Is.EqualTo(
                    "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'."
                    + " Candidate: the text at --line 116 in the edited file appears at lines 1, 3, 4 (first 3 matches) in the last compiled source."));
        }

        /// <summary>
        /// What: a resolve-failure Message stays unchanged when compiled source has no matching line.
        /// </summary>
        [Test]
        public void AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged_WhenNoLineMatches_ReturnsUnchanged()
        {
            const string message =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            string[] compiledLines =
            {
                "            return 0;"
            };

            string result = PausePointEnableWarnings.AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                116,
                "return 2;",
                compiledLines);

            Assert.That(result, Is.EqualTo(message));
        }

        /// <summary>
        /// What: a resolve-failure Message stays unchanged when the edited line text is blank.
        /// </summary>
        [Test]
        public void AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged_WhenEditedTextEmpty_ReturnsUnchanged()
        {
            const string message =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            string[] compiledLines =
            {
                "            return 2;"
            };

            string result = PausePointEnableWarnings.AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                116,
                "   ",
                compiledLines);

            Assert.That(result, Is.EqualTo(message));
        }

        /// <summary>
        /// What: a resolve-failure Message stays unchanged when compiled source lines are null.
        /// </summary>
        [Test]
        public void AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged_WhenCompiledSourceLinesNull_ReturnsUnchanged()
        {
            const string message =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";

            string result = PausePointEnableWarnings.AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                116,
                "return 2;",
                null);

            Assert.That(result, Is.EqualTo(message));
        }

        /// <summary>
        /// What: an active hot-reload gate plus a compiled-source match appends Candidate after
        /// Nearby methods on a resolve-failure Message.
        /// </summary>
        [Test]
        public void BuildResolveFailureMessage_WhenHotReloadGateTrueAndLineMatches_AppendsNearbyThenCandidate()
        {
            const string errorMessage =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            SourcePausePointNearbyCompiledMethod[] nearby =
            {
                new SourcePausePointNearbyCompiledMethod("Enemy.Update", 100, 120)
            };
            string[] compiledLines =
            {
                "            return 2;"
            };

            string result = PausePointEnableWarnings.BuildResolveFailureMessage(
                errorMessage,
                nearby,
                true,
                116,
                true,
                "return 2;",
                compiledLines);

            Assert.That(
                result,
                Is.EqualTo(
                    "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'."
                    + " Nearby methods in the last compiled source: 'Enemy.Update' spans lines 100-120."
                    + " Candidate: the text at --line 116 in the edited file appears at line 1 in the last compiled source."));
        }

        /// <summary>
        /// What: the same matching inputs without the hot-reload gate keep Nearby methods and omit
        /// Candidate.
        /// </summary>
        [Test]
        public void BuildResolveFailureMessage_WhenHotReloadGateFalseAndLineMatches_AppendsNearbyOnly()
        {
            const string errorMessage =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            SourcePausePointNearbyCompiledMethod[] nearby =
            {
                new SourcePausePointNearbyCompiledMethod("Enemy.Update", 100, 120)
            };
            string[] compiledLines =
            {
                "            return 2;"
            };

            string result = PausePointEnableWarnings.BuildResolveFailureMessage(
                errorMessage,
                nearby,
                false,
                116,
                true,
                "return 2;",
                compiledLines);

            Assert.That(
                result,
                Is.EqualTo(
                    "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'."
                    + " Nearby methods in the last compiled source: 'Enemy.Update' spans lines 100-120."));
        }

        /// <summary>
        /// What: a null compiled-source list under an active hot-reload gate keeps Nearby methods
        /// and omits Candidate.
        /// </summary>
        [Test]
        public void BuildResolveFailureMessage_WhenCompiledSourceLinesNull_AppendsNearbyOnly()
        {
            const string errorMessage =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            SourcePausePointNearbyCompiledMethod[] nearby =
            {
                new SourcePausePointNearbyCompiledMethod("Enemy.Update", 100, 120)
            };

            string result = PausePointEnableWarnings.BuildResolveFailureMessage(
                errorMessage,
                nearby,
                true,
                116,
                true,
                "return 2;",
                null);

            Assert.That(
                result,
                Is.EqualTo(
                    "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'."
                    + " Nearby methods in the last compiled source: 'Enemy.Update' spans lines 100-120."));
        }

        /// <summary>
        /// What: a failed edited-line read under an active hot-reload gate keeps Nearby methods and
        /// omits Candidate even when compiled source would match.
        /// </summary>
        [Test]
        public void BuildResolveFailureMessage_WhenRequestedLineReadFails_AppendsNearbyOnly()
        {
            const string errorMessage =
                "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'.";
            SourcePausePointNearbyCompiledMethod[] nearby =
            {
                new SourcePausePointNearbyCompiledMethod("Enemy.Update", 100, 120)
            };
            string[] compiledLines =
            {
                "            return 2;"
            };

            string result = PausePointEnableWarnings.BuildResolveFailureMessage(
                errorMessage,
                nearby,
                true,
                116,
                false,
                "return 2;",
                compiledLines);

            Assert.That(
                result,
                Is.EqualTo(
                    "No sequence point found on or after line 116 in 'Assets/Scripts/Enemy.cs'."
                    + " Nearby methods in the last compiled source: 'Enemy.Update' spans lines 100-120."));
        }

        /// <summary>
        /// What: retarget warning interpolates resolved method, requested line, and edited span.
        /// </summary>
        [Test]
        public void BuildRetargetedToHotReloadPatchWarningOrEmpty_WhenRetargeted_ReturnsFormattedWarning()
        {
            string warning = PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                true,
                "Example.Run",
                42,
                10,
                20);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadRetargetedToEditedFileWarningFormat,
                        "Example.Run",
                        42,
                        10,
                        20)));
        }

        /// <summary>
        /// What: the retarget helper stays silent when the marker did not retarget.
        /// </summary>
        [Test]
        public void BuildRetargetedToHotReloadPatchWarningOrEmpty_WhenNotRetargeted_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildRetargetedToHotReloadPatchWarningOrEmpty(
                false,
                "Example.Run",
                42,
                10,
                20);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: the PDB-unavailable helper interpolates method name and requested line.
        /// </summary>
        [Test]
        public void BuildPatchedMethodPdbUnavailableWarningOrEmpty_WhenUnavailable_ReturnsFormattedWarning()
        {
            string warning = PausePointEnableWarnings.BuildPatchedMethodPdbUnavailableWarningOrEmpty(
                true,
                "Example.Run",
                42);

            Assert.That(
                warning,
                Is.EqualTo(
                    string.Format(
                        SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                        "Example.Run",
                        42)));
        }

        /// <summary>
        /// What: the PDB-unavailable helper stays silent when the shim PDB is present.
        /// </summary>
        [Test]
        public void BuildPatchedMethodPdbUnavailableWarningOrEmpty_WhenAvailable_ReturnsEmpty()
        {
            string warning = PausePointEnableWarnings.BuildPatchedMethodPdbUnavailableWarningOrEmpty(
                false,
                "Example.Run",
                42);

            Assert.That(warning, Is.EqualTo(string.Empty));
        }

        /// <summary>
        /// What: nearby compiled spans are formatted as a suffix on a resolve-failure message.
        /// </summary>
        [Test]
        public void AppendNearbyCompiledMethodsSuffix_WhenNearbyMethodsExist_AppendsFormattedSpans()
        {
            string errorMessage = "No sequence point found on or after line 9999 in 'file'.";
            SourcePausePointNearbyCompiledMethod[] nearby =
            {
                new SourcePausePointNearbyCompiledMethod("CompiledMethodSpanFixture.Target", 8, 11),
                new SourcePausePointNearbyCompiledMethod("CompiledMethodSpanFixture.OtherMethod", 15, 18)
            };

            string message = PausePointEnableWarnings.AppendNearbyCompiledMethodsSuffix(errorMessage, nearby);

            Assert.That(
                message,
                Is.EqualTo(
                    errorMessage
                    + SourcePausePointConstants.NearbyCompiledMethodsPrefix
                    + "'CompiledMethodSpanFixture.Target' spans lines 8-11"
                    + "; "
                    + "'CompiledMethodSpanFixture.OtherMethod' spans lines 15-18"
                    + "."));
        }

        /// <summary>
        /// What: an empty nearby list leaves the resolve-failure message unchanged.
        /// </summary>
        [Test]
        public void AppendNearbyCompiledMethodsSuffix_WhenNearbyListIsEmpty_LeavesMessageUnchanged()
        {
            string errorMessage = "No sequence point found on or after line 9999 in 'file'.";

            string message = PausePointEnableWarnings.AppendNearbyCompiledMethodsSuffix(
                errorMessage,
                Array.Empty<SourcePausePointNearbyCompiledMethod>());

            Assert.That(message, Is.EqualTo(errorMessage));
        }

        /// <summary>
        /// What: enable resolve-failure on a real PDB fixture appends the nearest compiled
        /// method span from that file.
        /// </summary>
        [Test]
        public void Enable_WhenResolveFails_AppendsNearbyCompiledMethodSpans()
        {
            const string file =
                "Assets/Tests/Editor/SourcePausePointResolver/Fixtures/CompiledMethodSpanFixture.cs";
            SourcePausePointResolveResult otherMethod = SourcePausePointResolver.Resolve(file, 16);
            Assert.That(otherMethod.Success, Is.True, otherMethod.ErrorMessage);
            Assert.That(otherMethod.Resolution.CompiledMethodStartLine, Is.GreaterThan(0));
            Assert.That(otherMethod.Resolution.CompiledMethodEndLine, Is.GreaterThan(0));

            PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = file,
                Line = 9999,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });

            Assert.That(response.Success, Is.False);
            Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            string expectedMessage =
                "No sequence point found on or after line 9999 in '" + file + "'."
                + SourcePausePointConstants.NearbyCompiledMethodsPrefix
                + string.Format(
                    SourcePausePointConstants.NearbyCompiledMethodSpanFormat,
                    "CompiledMethodSpanFixture.OtherMethod",
                    otherMethod.Resolution.CompiledMethodStartLine,
                    otherMethod.Resolution.CompiledMethodEndLine)
                + ".";
            Assert.That(response.Message, Is.EqualTo(expectedMessage));
        }

        /// <summary>
        /// What: a resolve-failure enable response with active hot-reload patches uses the
        /// failure warning and next-action constants, not the success-path wording.
        /// </summary>
        [Test]
        public void Enable_WhenResolveFailsAndFileHasActivePatches_UsesResolveFailureWarningAndNextAction()
        {
            Func<string, HotReloadShimFileLookup> previous =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            HotReloadShimFileLookup stubLookup = new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                Array.Empty<byte>(),
                null,
                Array.Empty<HotReloadShimMethodLookup>());

            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = _ => stubLookup;
                PausePointResponse withPatches = EnableUnresolvableLine();

                Assert.That(withPatches.Success, Is.False);
                Assert.That(withPatches.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(withPatches.Warning, Is.EqualTo(ExpectedEnableResolveFailureWarning));
                Assert.That(
                    withPatches.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.HotReloadCompiledLineMapResolveFailureNextAction));

                HotReloadPausePointCoordination.GetShimLookupForFile = _ => null;
                PausePointResponse withoutPatches = EnableUnresolvableLine();

                Assert.That(withoutPatches.Success, Is.False);
                Assert.That(withoutPatches.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(withoutPatches.Warning, Is.EqualTo(string.Empty));
                Assert.That(
                    withoutPatches.RecommendedNextAction,
                    Is.EqualTo(SourcePausePointConstants.ResolveFailedRecommendedNextAction));
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previous;
            }
        }

        /// <summary>
        /// What: a line inside a patched method with no shim PDB is a distinct Kind, not
        /// NotInPatchedMethod.
        /// </summary>
        [Test]
        public void ShimResolve_WhenLineIsInPatchedMethodButPdbBytesAreMissing_ReturnsPatchedMethodPdbUnavailable()
        {
            SourcePausePointShimResolution resolution = SourcePausePointShimResolver.Resolve(
                CreatePdbUnavailableLookup(CompiledLineDriftProbeMethod(), 10),
                ResolveFailureFile,
                10);

            Assert.That(
                resolution.Kind,
                Is.EqualTo(SourcePausePointShimResolveKind.PatchedMethodPdbUnavailable));
            Assert.That(
                resolution.MethodDisplayName,
                Is.EqualTo("PausePointCompiledLineMapWarningTests.CompiledLineDriftProbe"));
        }

        /// <summary>
        /// What: a patched method whose shim lookup has no PDB bytes falls through to compiled
        /// resolve and emits the dedicated warning instead of the generic edited-file sentence.
        /// </summary>
        [Test]
        public void Enable_WhenPatchedMethodHasNoPdbBytes_EmitsDedicatedWarningOnSuccess()
        {
            string absolutePath = Path.Combine(
                UnityCliLoopPathResolver.GetProjectRoot(),
                ResolveFailureFile);
            string diskSource = File.ReadAllText(absolutePath);
            int requestedLine = FindLineNumberContaining(
                diskSource,
                "compiled-line-drift" + "-probe-unique") + 1;
            Assert.That(requestedLine, Is.GreaterThan(1));

            Func<string, HotReloadShimFileLookup> previous =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile =
                    _ => CreatePdbUnavailableLookup(CompiledLineDriftProbeMethod(), requestedLine);
                PausePointResponse response = new PausePointUseCase().Enable(new EnablePausePointSchema
                {
                    File = ResolveFailureFile,
                    Line = requestedLine,
                    TimeoutSeconds = 30,
                    Mode = UloopPausePointCaptureMode.SingleShot
                });

                Assert.That(
                    response.Success,
                    Is.True,
                    response.ErrorCode + " / " + response.Message + " / " + response.RecommendedNextAction);
                Assert.That(response.RetargetedToHotReloadPatch, Is.False);
                AssertLineBasis(response, "LastCompiledSource");
                string dedicatedWarning = string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                    "PausePointCompiledLineMapWarningTests.CompiledLineDriftProbe",
                    requestedLine);
                string expectedWarning = PausePointEnableWarnings.MergeWarnings(
                    PausePointEnableWarnings.MergeWarnings(
                        PausePointEnableWarnings.CreateEnableWarning(),
                        dedicatedWarning),
                    SourcePausePointConstants.SmallMethodInliningRiskWarning);
                Assert.That(response.Warning, Is.EqualTo(expectedWarning));
                Assert.That(response.Warning, Does.Not.Contain(GenericPatchedMethodsUseEditedFileSentence));
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previous;
            }
        }

        /// <summary>
        /// What: resolve failure inside a patched method with no shim PDB uses the dedicated
        /// warning, not the generic compiled-line-map failure sentence.
        /// </summary>
        [Test]
        public void Enable_WhenPatchedMethodHasNoPdbBytes_EmitsDedicatedWarningOnResolveFailure()
        {
            Func<string, HotReloadShimFileLookup> previous =
                HotReloadPausePointCoordination.GetShimLookupForFile;
            try
            {
                HotReloadPausePointCoordination.GetShimLookupForFile =
                    _ => CreatePdbUnavailableLookup(CompiledLineDriftProbeMethod(), UnresolvableLine);
                PausePointResponse response = EnableUnresolvableLine();

                Assert.That(response.Success, Is.False);
                Assert.That(response.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
                Assert.That(
                    response.Warning,
                    Is.EqualTo(
                        string.Format(
                            SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                            "PausePointCompiledLineMapWarningTests.CompiledLineDriftProbe",
                            UnresolvableLine)));
                Assert.That(response.Warning, Does.Not.Contain(GenericPatchedMethodsUseEditedFileSentence));
            }
            finally
            {
                HotReloadPausePointCoordination.GetShimLookupForFile = previous;
            }
        }

        internal static int CompiledLineDriftProbe()
        {
            // compiled-line-drift-probe-unique
            return 424242;
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

        // Confirms a source-location enable response reports the resolver basis callers must use.
        private static void AssertLineBasis(PausePointResponse response, string expected)
        {
            Assert.That(response.LineBasis, Is.EqualTo(expected));
        }

        private static PausePointResponse EnableUnresolvableLine()
        {
            return new PausePointUseCase().Enable(new EnablePausePointSchema
            {
                File = ResolveFailureFile,
                Line = UnresolvableLine,
                TimeoutSeconds = 30,
                Mode = UloopPausePointCaptureMode.SingleShot
            });
        }

        private static HotReloadShimFileLookup CreatePdbUnavailableLookup(MethodBase patchedMethod, int line)
        {
            HotReloadShimMethodLookup[] methods =
            {
                new HotReloadShimMethodLookup(
                    patchedMethod,
                    patchedMethod,
                    false,
                    line,
                    line)
            };
            return new HotReloadShimFileLookup(
                Array.Empty<byte>(),
                null,
                null,
                methods);
        }

        private static MethodBase CompiledLineDriftProbeMethod()
        {
            MethodBase method = typeof(PausePointCompiledLineMapWarningTests).GetMethod(
                nameof(CompiledLineDriftProbe),
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return method;
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
                // Why zero: Unity's isPaused is a bool; Option B Resume must fully clear pause.
                PauseCount = 0;
            }
        }
    }
}
