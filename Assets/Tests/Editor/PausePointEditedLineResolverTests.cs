using System;
using System.Collections.Generic;
using System.Linq;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies how an edited --line is resolved through the line map: mapped statements arm
    /// with edited-file lines, and uncompiled lines are refused instead of arming another statement.
    /// </summary>
    [TestFixture]
    public sealed class PausePointEditedLineResolverTests
    {
        private const string TestFile = "Assets/Fixture/Owner.cs";
        private const string LineNotCompiledErrorCode = "PAUSE_POINT_LINE_NOT_COMPILED";

        // Line numbers of the compiled source, which every expected value below depends on:
        //  1 using System;              10 public int Second()
        //  2 namespace Fixture          11 {
        //  3 {                          12 // compute
        //  4 public sealed class Owner  13 int a = 2;
        //  5 {                          14 (blank)
        //  6 public int First()         15 return a;
        //  7 {                          16 }
        //  8 return 1;                  17 } (class)
        //  9 }                          18 } (namespace)
        // Method spans: First 6-9, Second 10-16. Sequence points: 7, 8, 9, 11, 13, 15, 16.
        private static readonly string[] CompiledLines =
        {
            "using System;",
            "namespace Fixture",
            "{",
            "    public sealed class Owner",
            "    {",
            "        public int First()",
            "        {",
            "            return 1;",
            "        }",
            "        public int Second()",
            "        {",
            "            // compute",
            "            int a = 2;",
            "",
            "            return a;",
            "        }",
            "    }",
            "}",
        };

        private static readonly FakeMethodSpan[] MethodSpans =
        {
            new FakeMethodSpan("First", 6, 9),
            new FakeMethodSpan("Second", 10, 16),
        };

        [SetUp]
        public void SetUp()
        {
            UloopPausePointRegistry.ConfigureForTests(new FakePausePointPauseController(), () => DateTime.UtcNow);
        }

        [TearDown]
        public void TearDown()
        {
            UloopPausePointRegistry.ResetForTests();
        }

        /// <summary>
        /// What: an unchanged file resolves a statement to the same line on the edited-file basis.
        /// </summary>
        [Test]
        public void Identity_Statement_ResolvesToTheSameLineWithEditedFileBasis()
        {
            PausePointEditedLineResolution resolution = Resolve(CompiledLines, 13);

            AssertResolved(resolution, compiledLine: 13, editedLine: 13);
            Assert.That(resolution.EditedMethodStartLine, Is.EqualTo(10));
            Assert.That(resolution.EditedMethodEndLine, Is.EqualTo(16));
            Assert.That(resolution.LineBasis, Is.EqualTo("EditedFile"));
            Assert.That(resolution.Warning, Is.Empty);
        }

        /// <summary>
        /// What: a line without a sequence point in an unchanged file rounds forward to the next
        /// compiled statement, as the compiled resolver does.
        /// </summary>
        [Test]
        public void Identity_LineWithoutSequencePoint_RoundsForwardToTheNextCompiledLine()
        {
            AssertResolved(Resolve(CompiledLines, 12), compiledLine: 13, editedLine: 13);
        }

        /// <summary>
        /// What: a line with no sequence point at or after it returns a resolve failure that names
        /// the requested line of the file on disk and lists the nearby spans in edited-file lines,
        /// so the use case builds the RESOLVE_FAILED response on the edited-file basis.
        /// </summary>
        [Test]
        public void Identity_NoSequencePointAtOrAfterLine_ReportsTheRequestedLineAndNearbySpans()
        {
            PausePointEditedLineResolution resolution = Resolve(CompiledLines, 17);

            AssertUnresolved(resolution);
            Assert.That(
                resolution.ResolveResult.ErrorMessage,
                Is.EqualTo(string.Format(
                    SourcePausePointConstants.ResolveFailedNoCompiledStatementInEditedFileMessageFormat,
                    17,
                    TestFile)));
            Assert.That(NearbyOf(resolution), Is.EqualTo(new[] { "First 6-9", "Second 10-16" }));
        }

        /// <summary>
        /// What: after a top-of-file insert, a resolve failure names the requested edited line and
        /// lists the nearby spans shifted to edited-file lines instead of their compiled lines.
        /// </summary>
        [Test]
        public void Shifted_NoSequencePointAtOrAfterLine_ReportsTheEditedLineAndEditedNearbySpans()
        {
            PausePointEditedLineResolution resolution = Resolve(Shifted(), 20);

            AssertUnresolved(resolution);
            Assert.That(
                resolution.ResolveResult.ErrorMessage,
                Is.EqualTo(string.Format(
                    SourcePausePointConstants.ResolveFailedNoCompiledStatementInEditedFileMessageFormat,
                    20,
                    TestFile)));
            Assert.That(NearbyOf(resolution), Is.EqualTo(new[] { "First 9-12", "Second 13-19" }));
        }

        /// <summary>
        /// What: a nearby span whose start line was removed since the last compile is left out,
        /// because its edited range is unknown and a compiled range beside edited ones would be
        /// read as edited.
        /// </summary>
        [Test]
        public void RemovedMethodHeader_NoSequencePointAtOrAfterLine_DropsNearbySpansWhoseBoundaryWasRemoved()
        {
            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(
                Join(CompiledLines),
                Join(RemovedMethodHeader()));
            Assert.That(map.ToEditedLineOrZero(10), Is.EqualTo(0), "Second's declaration must have no edited line.");
            Assert.That(map.ToEditedLineOrZero(16), Is.EqualTo(15), "Second's closing brace must stay mapped.");

            PausePointEditedLineResolution resolution = Resolve(RemovedMethodHeader(), 16);

            AssertUnresolved(resolution);
            Assert.That(
                resolution.ResolveResult.ErrorMessage,
                Is.EqualTo(string.Format(
                    SourcePausePointConstants.ResolveFailedNoCompiledStatementInEditedFileMessageFormat,
                    16,
                    TestFile)));
            Assert.That(NearbyOf(resolution), Is.EqualTo(new[] { "First 6-9" }));
        }

        /// <summary>
        /// What: a resolve failure that names no line, such as missing symbols, is returned as the
        /// resolver reported it, because it has no compiled line to translate.
        /// </summary>
        [Test]
        public void Shifted_FailureWithoutALine_IsReturnedUnchanged()
        {
            PausePointEditedLineResolution resolution = Resolve(Shifted(), 16, outcome: FakeOutcome.SymbolsUnavailable);

            Assert.That(resolution.Refusal, Is.Null);
            Assert.That(resolution.ResolveResult.Success, Is.False);
            Assert.That(
                resolution.ResolveResult.FailureReason,
                Is.EqualTo(SourcePausePointResolveFailureReason.SymbolsUnavailable));
            Assert.That(resolution.ResolveResult.ErrorMessage, Is.EqualTo("symbols"));
        }

        /// <summary>
        /// What: after a top-of-file insert, a statement that always throws is reported with the
        /// edited line of that statement, not its compiled line or the requested line.
        /// </summary>
        [Test]
        public void Shifted_PostLineAlwaysThrows_ReportsTheEditedStatementLine()
        {
            PausePointEditedLineResolution resolution = Resolve(Shifted(), 15, outcome: FakeOutcome.PostLineAlwaysThrows);

            Assert.That(resolution.Refusal, Is.Null);
            Assert.That(resolution.ResolveResult.Success, Is.False);
            Assert.That(
                resolution.ResolveResult.FailureReason,
                Is.EqualTo(SourcePausePointResolveFailureReason.PostLineAlwaysThrows));
            Assert.That(
                resolution.ResolveResult.ErrorMessage,
                Is.EqualTo(string.Format(SourcePausePointConstants.PostLineAlwaysThrowsMessageFormat, 16, TestFile)));
            Assert.That(resolution.ResolveResult.StatementLine, Is.EqualTo(16));
            Assert.That(resolution.LineBasis, Is.EqualTo("EditedFile"));
        }

        /// <summary>
        /// What: a statement that always throws but was changed since the last compile is refused
        /// as not compiled, because the throwing statement no longer exists in the edited file.
        /// </summary>
        [Test]
        public void Changed_PostLineAlwaysThrowsOnAChangedStatement_RefusesStatementRemoved()
        {
            PausePointEditedLineResolution resolution = Resolve(ChangedR(), 14, outcome: FakeOutcome.PostLineAlwaysThrows);

            AssertLineNotCompiled(resolution, "resolves to the compiled statement 'return a;', which no longer exists");
        }

        /// <summary>
        /// What: a statement that always throws behind a statement inserted after the last compile
        /// is refused naming the inserted statement, as a statement that does not throw would be.
        /// </summary>
        [Test]
        public void Inserted_PostLineAlwaysThrowsBehindAnUncompiledStatement_RefusesTheUncompiledStatement()
        {
            PausePointEditedLineResolution resolution = Resolve(Inserted(), 14, outcome: FakeOutcome.PostLineAlwaysThrows);

            AssertLineNotCompiled(resolution, "the next statement, line 15 ('a += 1;'), is not in the last compiled source");
        }

        /// <summary>
        /// What: a statement that always throws inside a hot-reload patched method is refused as
        /// patched with the method's edited range, as the patcher refuses a statement there that
        /// does not throw, instead of pointing at a timing whose retry is refused that way anyway.
        /// </summary>
        [Test]
        public void Identity_PostLineAlwaysThrowsInsideAPatchedSpan_RefusesAsPatchedByHotReload()
        {
            PausePointEditedLineResolution resolution = Resolve(
                CompiledLines,
                12,
                patchedSpanOrNull: line => line >= 13 && line <= 16 ? new PausePointPatchedEditedSpan("Owner.Second", 13, 16) : null,
                outcome: FakeOutcome.PostLineAlwaysThrows);

            AssertPatchedByHotReload(
                resolution,
                string.Format(SourcePausePointConstants.HotReloadPatchedMethodRefusalMessageFormat, 12, "Owner.Second", 13, 16),
                string.Format(SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat, 12, "Owner.Second", 13, 16));
        }

        /// <summary>
        /// What: a line past the end of the edited file is refused as not compiled, names the
        /// file's line count, and points at the valid line range instead of a hot reload or compile
        /// that cannot change the line count, while the last line still reaches the resolver.
        /// </summary>
        [Test]
        public void Identity_LineBeyondEndOfFile_RefusesLineNotCompiled()
        {
            foreach (int line in new[] { 40, 19 })
            {
                PausePointEditedLineResolution resolution = Resolve(CompiledLines, line);

                Assert.That(resolution.ResolveResult, Is.Null);
                Assert.That(resolution.Refusal, Is.Not.Null);
                Assert.That(resolution.Refusal.ErrorCode, Is.EqualTo(LineNotCompiledErrorCode));
                Assert.That(resolution.Refusal.Message, Does.Contain("beyond the end"));
                Assert.That(resolution.Refusal.Message, Does.Contain("(18 lines)"));
                Assert.That(
                    resolution.Refusal.RecommendedNextAction,
                    Is.EqualTo(string.Format(
                        SourcePausePointConstants.LineNotCompiledBeyondEndOfFileRecommendedNextActionFormat,
                        18)));
            }

            AssertUnresolved(Resolve(CompiledLines, 18));
        }

        /// <summary>
        /// What: a statement with a --method filter naming its own method arms that statement.
        /// </summary>
        [Test]
        public void Identity_Statement_WithMatchingMethod_Arms()
        {
            AssertResolved(Resolve(CompiledLines, 13, method: "Second"), compiledLine: 13, editedLine: 13);
        }

        /// <summary>
        /// What: a statement with a --method filter naming another method returns a resolve failure
        /// that names the method and the requested line of the file on disk.
        /// </summary>
        [Test]
        public void Identity_Statement_WithNonMatchingMethod_ReportsTheMethodNameAndTheRequestedLine()
        {
            PausePointEditedLineResolution resolution = Resolve(CompiledLines, 13, method: "First");

            AssertUnresolved(resolution);
            Assert.That(
                resolution.ResolveResult.ErrorMessage,
                Is.EqualTo(string.Format(
                    SourcePausePointConstants.ResolveFailedNoMethodNamedInEditedFileMessageFormat,
                    "First",
                    13,
                    TestFile)));
        }

        /// <summary>
        /// What: when the resolved line lies inside a hot-reload patched span, the compiled
        /// resolution is returned so the patcher can refuse it with its own guidance.
        /// </summary>
        [Test]
        public void Identity_ResolvedLineInsidePatchedSpan_ReturnsTheCompiledResolutionForPatchToRefuse()
        {
            PausePointEditedLineResolution resolution = Resolve(
                CompiledLines,
                13,
                patchedSpanOrNull: line => line >= 10 && line <= 16 ? new PausePointPatchedEditedSpan("Owner.Second", 10, 16) : null);

            AssertResolved(resolution, compiledLine: 13, editedLine: 13);
        }

        /// <summary>
        /// What: after three lines are inserted at the top, the edited statement arms its compiled
        /// line and reports the edited line and edited method span.
        /// </summary>
        [Test]
        public void Shifted_Statement_ArmsTheShiftedCompiledLineAndReportsTheEditedLine()
        {
            PausePointEditedLineResolution resolution = Resolve(Shifted(), 16);

            AssertResolved(resolution, compiledLine: 13, editedLine: 16);
            Assert.That(resolution.EditedResolvedEndLine, Is.EqualTo(16));
            Assert.That(resolution.EditedMethodStartLine, Is.EqualTo(13));
            Assert.That(resolution.EditedMethodEndLine, Is.EqualTo(19));
        }

        /// <summary>
        /// What: after a top-of-file insert, a comment line rounds forward in compiled coordinates
        /// and reports the edited line of the statement it rounded to.
        /// </summary>
        [Test]
        public void Shifted_LineWithoutSequencePoint_RoundsForwardInCompiledCoordinatesAndReportsTheEditedLine()
        {
            AssertResolved(Resolve(Shifted(), 15), compiledLine: 13, editedLine: 16);
        }

        /// <summary>
        /// What: after a top-of-file insert, a --method filter naming the statement's method arms it.
        /// </summary>
        [Test]
        public void Shifted_Statement_WithMatchingMethod_Arms()
        {
            AssertResolved(Resolve(Shifted(), 16, method: "Second"), compiledLine: 13, editedLine: 16);
        }

        /// <summary>
        /// What: after a top-of-file insert, a --method filter naming another method returns a
        /// resolve failure that names the requested edited line and the edited nearby spans.
        /// </summary>
        [Test]
        public void Shifted_Statement_WithNonMatchingMethod_ReportsTheEditedLineAndEditedNearbySpans()
        {
            PausePointEditedLineResolution resolution = Resolve(Shifted(), 16, method: "First");

            AssertUnresolved(resolution);
            Assert.That(
                resolution.ResolveResult.ErrorMessage,
                Is.EqualTo(string.Format(
                    SourcePausePointConstants.ResolveFailedNoMethodNamedInEditedFileMessageFormat,
                    "First",
                    16,
                    TestFile)));
            Assert.That(NearbyOf(resolution), Is.EqualTo(new[] { "First 9-12", "Second 13-19" }));
        }

        /// <summary>
        /// What: a statement changed after the last compile is refused as not compiled, naming its text.
        /// </summary>
        [Test]
        public void Changed_Statement_RefusesLineNotCompiled()
        {
            PausePointEditedLineResolution resolution = Resolve(ChangedA(), 13);

            AssertLineNotCompiled(resolution, "Line 13 of '" + TestFile + "' ('int a = 20;') is not in the last compiled source");
        }

        /// <summary>
        /// What: a blank line before a statement inserted after the last compile is refused and
        /// names the inserted statement, instead of rounding past it to the old statement.
        /// </summary>
        [Test]
        public void Inserted_BlankLineBeforeTheInsertedStatement_RefusesNamingTheInsertedLine()
        {
            PausePointEditedLineResolution resolution = Resolve(Inserted(), 14);

            AssertLineNotCompiled(resolution, "the next statement, line 15 ('a += 1;'), is not in the last compiled source");
        }

        /// <summary>
        /// What: a blank line whose compiled successor statement was changed is refused because the
        /// statement it rounds to no longer exists in the edited file.
        /// </summary>
        [Test]
        public void Changed_LineWithoutSequencePointWhoseCompiledSuccessorChanged_RefusesLineNotCompiled()
        {
            PausePointEditedLineResolution resolution = Resolve(ChangedR(), 14);

            AssertLineNotCompiled(resolution, "resolves to the compiled statement 'return a;', which no longer exists");
        }

        /// <summary>
        /// What: a changed statement is refused even when --method names its method.
        /// </summary>
        [Test]
        public void Changed_Statement_WithMethod_StillRefuses()
        {
            AssertLineNotCompiled(Resolve(ChangedA(), 13, method: "Second"), "is not in the last compiled source");
        }

        /// <summary>
        /// What: when the uncompiled statement blocking the request lies inside a method hot reload
        /// added, the refusal names that method and the blocking line, and does not claim the
        /// requested line itself is inside the added method.
        /// </summary>
        [Test]
        public void Inserted_BlockerInsideAnAddedMethod_RefusesWithTheAddedMethodLabel()
        {
            PausePointEditedLineResolution resolution = Resolve(
                Inserted(),
                14,
                addedMethodOrNull: line => line == 15
                    ? new HotReloadAddedMethodAtLine("Fixture.Owner.Added()", "Added", "Owner", null)
                    : null);

            Assert.That(resolution.Refusal, Is.Not.Null);
            Assert.That(resolution.ResolveResult, Is.Null);
            Assert.That(resolution.Refusal.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(resolution.Refusal.Message, Does.Contain("Fixture.Owner.Added()"));
            Assert.That(resolution.Refusal.Message, Does.Contain("line 15"));
            Assert.That(resolution.Refusal.Message, Does.Not.Contain("Line 14 is inside"));
            Assert.That(
                resolution.Refusal.RecommendedNextAction,
                Is.EqualTo(SourcePausePointConstants.AddedMethodResolveFailureNextAction));
        }

        /// <summary>
        /// What: a blank line whose next uncompiled statement is inside a hot-reload patched method
        /// is refused as patched by hot reload with the method's edited range, not as not compiled,
        /// because hot-reloading an already patched method would return the same refusal.
        /// </summary>
        [Test]
        public void PatchedBodyAfterBlank_BlockerInsideAPatchedSpan_RefusesAsPatchedByHotReloadWithTheEditedRange()
        {
            PausePointEditedLineResolution resolution = Resolve(
                PatchedBodyAfterBlank(),
                10,
                patchedSpanOrNull: line => line >= 11 && line <= 16 ? PatchedSecondSpan() : null);

            AssertPatchedByHotReload(
                resolution,
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodNextStatementRefusalMessageFormat,
                    10,
                    11,
                    "Owner.Second",
                    11,
                    16),
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat,
                    10,
                    "Owner.Second",
                    11,
                    16));
        }

        /// <summary>
        /// What: the same blank line is still refused as not compiled when the uncompiled
        /// statement after it is outside every patched method.
        /// </summary>
        [Test]
        public void PatchedBodyAfterBlank_BlockerOutsideEveryPatchedSpan_StillRefusesLineNotCompiled()
        {
            PausePointEditedLineResolution resolution = Resolve(PatchedBodyAfterBlank(), 10);

            AssertLineNotCompiled(resolution, "the next statement, line 11 ('public int Second() // patched')");
        }

        /// <summary>
        /// What: an uncompiled requested line that is itself inside a patched method is refused
        /// with the message that places the requested line in that method.
        /// </summary>
        [Test]
        public void PatchedBodyAfterBlank_RequestedLineItselfInsideAPatchedSpan_UsesTheSingleLineMessage()
        {
            PausePointEditedLineResolution resolution = Resolve(
                PatchedBodyAfterBlank(),
                13,
                patchedSpanOrNull: line => line >= 11 && line <= 16 ? PatchedSecondSpan() : null);

            AssertPatchedByHotReload(
                resolution,
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalMessageFormat,
                    13,
                    "Owner.Second",
                    11,
                    16),
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat,
                    13,
                    "Owner.Second",
                    11,
                    16));
        }

        /// <summary>
        /// What: when the blocking statement is inside both a hot-reload added method and a patched
        /// span, the added-method refusal wins, because only a compile gives an added method
        /// compiled code to arm.
        /// </summary>
        [Test]
        public void PatchedBodyAfterBlank_BlockerInsideBothAnAddedMethodAndAPatchedSpan_PrefersTheAddedMethodRefusal()
        {
            PausePointEditedLineResolution resolution = Resolve(
                PatchedBodyAfterBlank(),
                10,
                patchedSpanOrNull: line => line >= 11 && line <= 16 ? PatchedSecondSpan() : null,
                addedMethodOrNull: line => line == 11
                    ? new HotReloadAddedMethodAtLine("Fixture.Owner.Second()", "Second", "Owner", null)
                    : null);

            Assert.That(resolution.ResolveResult, Is.Null);
            Assert.That(resolution.Refusal, Is.Not.Null);
            Assert.That(resolution.Refusal.ErrorCode, Is.EqualTo(SourcePausePointConstants.ErrorCodeResolveFailed));
            Assert.That(resolution.Refusal.Message, Does.Contain("which hot reload added"));
            Assert.That(
                resolution.Refusal.RecommendedNextAction,
                Is.EqualTo(SourcePausePointConstants.AddedMethodResolveFailureNextAction));
        }

        /// <summary>
        /// What: when the resolved statement is outside every patched span but the uncompiled
        /// statement between the requested line and it is inside one, the refusal is patched by
        /// hot reload with that span's edited range.
        /// </summary>
        [Test]
        public void Inserted_BlockerInsideAPatchedSpanBehindTheResolvedLine_RefusesAsPatchedByHotReload()
        {
            PausePointEditedLineResolution resolution = Resolve(
                Inserted(),
                14,
                patchedSpanOrNull: line => line == 15 ? new PausePointPatchedEditedSpan("Owner.Second", 15, 15) : null);

            AssertPatchedByHotReload(
                resolution,
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodNextStatementRefusalMessageFormat,
                    14,
                    15,
                    "Owner.Second",
                    15,
                    15),
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat,
                    14,
                    "Owner.Second",
                    15,
                    15));
        }

        /// <summary>
        /// What: a comment line whose compiled successor statement was removed is refused instead
        /// of arming the removed statement.
        /// </summary>
        [Test]
        public void Removed_LineWithoutSequencePointWhoseCompiledSuccessorWasRemoved_RefusesLineNotCompiled()
        {
            PausePointEditedLineResolution resolution = Resolve(Removed(), 12);

            AssertLineNotCompiled(resolution, "resolves to the compiled statement 'int a = 2;', which no longer exists");
        }

        /// <summary>
        /// What: a line inside a type appended after the last compiled line has no compiled line
        /// at or after it, so it is refused instead of resolving compiled line 0.
        /// </summary>
        [Test]
        public void Appended_LineInsideTheAppendedType_RefusesNoCompiledLineAtOrAfter()
        {
            PausePointEditedLineResolution resolution = Resolve(Appended(), 20);

            AssertLineNotCompiled(resolution, "No compiled line exists at or after line 20 of '" + TestFile + "'");
        }

        /// <summary>
        /// What: when the rounded-to statement lies inside a hot-reload patched span, the check for
        /// uncompiled statements in between is skipped so the patcher refuses with its guidance.
        /// </summary>
        [Test]
        public void Inserted_ResolvedLineInsidePatchedSpan_SkipsTheBlockerCheck()
        {
            PausePointEditedLineResolution resolution = Resolve(
                Inserted(),
                14,
                patchedSpanOrNull: line => line >= 10 && line <= 17 ? new PausePointPatchedEditedSpan("Owner.Second", 10, 17) : null);

            AssertResolved(resolution, compiledLine: 15, editedLine: 16);
        }

        private static PausePointEditedLineResolution Resolve(
            IReadOnlyList<string> editedLines,
            int line,
            string method = "",
            Func<int, PausePointPatchedEditedSpan> patchedSpanOrNull = null,
            Func<int, HotReloadAddedMethodAtLine> addedMethodOrNull = null,
            FakeOutcome outcome = FakeOutcome.Resolve)
        {
            PausePointEditedLineMap map = PausePointEditedLineMap.BuildOrNull(Join(CompiledLines), Join(editedLines));
            EnablePausePointSchema parameters = new EnablePausePointSchema { File = TestFile, Line = line, Method = method };
            FakeCompiledResolver resolver = new FakeCompiledResolver(CompiledLines, MethodSpans, outcome);
            PausePointEditedLineResolveContext context = new PausePointEditedLineResolveContext(
                map,
                TestFile,
                parameters,
                compiledLine => resolver.Resolve(compiledLine, method),
                patchedSpanOrNull ?? (_ => null),
                addedMethodOrNull ?? (_ => null));
            return PausePointEditedLineResolver.ResolveThroughMap(context);
        }

        private static void AssertResolved(PausePointEditedLineResolution resolution, int compiledLine, int editedLine)
        {
            Assert.That(resolution.Refusal, Is.Null);
            Assert.That(resolution.ResolveResult, Is.Not.Null);
            Assert.That(resolution.ResolveResult.Success, Is.True);
            Assert.That(resolution.ResolveResult.Resolution.ResolvedLine, Is.EqualTo(compiledLine));
            Assert.That(resolution.EditedResolvedLine, Is.EqualTo(editedLine));
            Assert.That(resolution.LineBasis, Is.EqualTo("EditedFile"));
        }

        private static void AssertUnresolved(PausePointEditedLineResolution resolution)
        {
            Assert.That(resolution.Refusal, Is.Null);
            Assert.That(resolution.ResolveResult, Is.Not.Null);
            Assert.That(resolution.ResolveResult.Success, Is.False);
            Assert.That(
                resolution.ResolveResult.FailureReason,
                Is.EqualTo(SourcePausePointResolveFailureReason.NoSequencePointOnOrAfterLine));
        }

        // Nearby spans as "Name start-end", so a test compares the whole list at once.
        private static string[] NearbyOf(PausePointEditedLineResolution resolution)
        {
            return resolution.ResolveResult.NearbyCompiledMethods
                .Select(nearby => nearby.DisplayName + " " + nearby.StartLine + "-" + nearby.EndLine)
                .ToArray();
        }

        private static void AssertLineNotCompiled(PausePointEditedLineResolution resolution, string expectedMessagePart)
        {
            Assert.That(resolution.ResolveResult, Is.Null);
            Assert.That(resolution.Refusal, Is.Not.Null);
            Assert.That(resolution.Refusal.Success, Is.False);
            Assert.That(resolution.Refusal.ErrorCode, Is.EqualTo(LineNotCompiledErrorCode));
            Assert.That(resolution.Refusal.Message, Does.Contain(expectedMessagePart));
            Assert.That(
                resolution.Refusal.RecommendedNextAction,
                Is.EqualTo(SourcePausePointConstants.LineNotCompiledRecommendedNextAction));
        }

        private static void AssertPatchedByHotReload(
            PausePointEditedLineResolution resolution,
            string expectedMessage,
            string expectedNextAction)
        {
            Assert.That(resolution.ResolveResult, Is.Null);
            Assert.That(resolution.Refusal, Is.Not.Null);
            Assert.That(resolution.Refusal.Success, Is.False);
            Assert.That(
                resolution.Refusal.ErrorCode,
                Is.EqualTo(SourcePausePointConstants.ErrorCodePausePointPatchedByHotReload));
            Assert.That(resolution.Refusal.Message, Is.EqualTo(expectedMessage));
            Assert.That(resolution.Refusal.RecommendedNextAction, Is.EqualTo(expectedNextAction));
        }

        // Three "// pad" lines after line 1, so every compiled line from 2 on sits 3 lines lower.
        private static string[] Shifted()
        {
            List<string> lines = CompiledLines.ToList();
            lines.InsertRange(1, new[] { "// pad", "// pad", "// pad" });
            return lines.ToArray();
        }

        private static string[] ChangedA()
        {
            return ReplaceLine(13, "            int a = 20;");
        }

        private static string[] ChangedR()
        {
            return ReplaceLine(15, "            return a + 1;");
        }

        // Edited 15 is the inserted "a += 1;" and edited 16 is the compiled "return a;".
        private static string[] Inserted()
        {
            List<string> lines = CompiledLines.ToList();
            lines.Insert(14, "            a += 1;");
            return lines.ToArray();
        }

        // A blank line is inserted before Second, and Second's declaration and statements are
        // rewritten as a patched method body would be, so edited 10, 11, 13 and 15 have no
        // compiled twin. Edited 12 is the compiled brace 11 and edited 14 is the compiled blank 14.
        private static string[] PatchedBodyAfterBlank()
        {
            List<string> lines = CompiledLines.Take(9).ToList();
            lines.Add(string.Empty);
            lines.Add("        public int Second() // patched");
            lines.Add("        {");
            lines.Add("            int b = 3;");
            lines.Add(string.Empty);
            lines.Add("            return b;");
            lines.AddRange(CompiledLines.Skip(15));
            return lines.ToArray();
        }

        // The edited span hot reload reports for Second in PatchedBodyAfterBlank.
        private static PausePointPatchedEditedSpan PatchedSecondSpan()
        {
            return new PausePointPatchedEditedSpan("Owner.Second", 11, 16);
        }

        // Edited 13 is the compiled blank line 14 and edited 14 is the compiled "return a;".
        private static string[] Removed()
        {
            List<string> lines = CompiledLines.ToList();
            lines.RemoveAt(12);
            return lines.ToArray();
        }

        // Second's declaration (compiled 10) is removed, so Second's start has no edited line and
        // edited 10-17 are compiled 11-18.
        private static string[] RemovedMethodHeader()
        {
            List<string> lines = CompiledLines.ToList();
            lines.RemoveAt(9);
            return lines.ToArray();
        }

        // Edited 19-22 follow the compiled namespace brace, so none of them has a compiled line.
        private static string[] Appended()
        {
            return CompiledLines.Concat(new[] { "", "internal sealed class Extra", "{", "}" }).ToArray();
        }

        private static string[] ReplaceLine(int line, string text)
        {
            string[] lines = (string[])CompiledLines.Clone();
            lines[line - 1] = text;
            return lines;
        }

        private static string Join(IEnumerable<string> lines)
        {
            return string.Join("\n", lines) + "\n";
        }

        // Refusal responses capture the editor state, which reads the registry's pause controller.
        private sealed class FakePausePointPauseController : IUloopPausePointPauseController
        {
            public bool IsPlaying => true;
            public bool IsPaused => false;

            public void Pause()
            {
            }

            public void Resume()
            {
            }
        }

        // How the fake resolver answers: resolve the statement a compiled line rounds to, report
        // that statement as always throwing, or fail before reading lines as missing symbols do.
        private enum FakeOutcome
        {
            Resolve,
            PostLineAlwaysThrows,
            SymbolsUnavailable,
        }

        private sealed class FakeMethodSpan
        {
            public string Name { get; }
            public int Start { get; }
            public int End { get; }

            public FakeMethodSpan(string name, int start, int end)
            {
                Name = name;
                Start = start;
                End = end;
            }
        }

        /// <summary>
        /// Stands in for the PDB-backed resolver: returns the first sequence point at or after a
        /// compiled line, where a sequence point is any non-blank, non-comment line from a method's
        /// opening brace (the line after its declaration) to its closing brace. When none is found,
        /// the failure lists every method span as nearby, in compiled lines.
        /// </summary>
        private sealed class FakeCompiledResolver
        {
            private readonly IReadOnlyList<string> _compiledLines;
            private readonly IReadOnlyList<FakeMethodSpan> _spans;
            private readonly FakeOutcome _outcome;

            public FakeCompiledResolver(
                IReadOnlyList<string> compiledLines,
                IReadOnlyList<FakeMethodSpan> spans,
                FakeOutcome outcome)
            {
                _compiledLines = compiledLines;
                _spans = spans;
                _outcome = outcome;
            }

            public SourcePausePointResolveResult Resolve(int compiledLine, string method)
            {
                if (_outcome == FakeOutcome.SymbolsUnavailable)
                {
                    return SourcePausePointResolveResult.Failure(
                        SourcePausePointResolveFailureReason.SymbolsUnavailable, "symbols");
                }

                for (int line = compiledLine; line <= _compiledLines.Count; line++)
                {
                    FakeMethodSpan span = SpanWithSequencePointAtOrNull(line);
                    if (span == null)
                    {
                        continue;
                    }

                    if (!string.IsNullOrEmpty(method) && span.Name != method)
                    {
                        continue;
                    }

                    if (_outcome == FakeOutcome.PostLineAlwaysThrows)
                    {
                        return SourcePausePointResolveResult.Failure(
                            SourcePausePointResolveFailureReason.PostLineAlwaysThrows,
                            "post",
                            statementLine: line);
                    }

                    return SourcePausePointResolveResult.SuccessResult(new SourcePausePointResolution(
                        "Fixture",
                        "mvid",
                        1,
                        span.Name,
                        false,
                        false,
                        0,
                        0,
                        SourcePausePointSnapshotTiming.PreLine,
                        line,
                        line,
                        span.Start,
                        span.End,
                        Array.Empty<SourcePausePointLocalVariable>(),
                        Array.Empty<SourcePausePointParameter>(),
                        Array.Empty<string>()));
                }

                return SourcePausePointResolveResult.Failure(
                    SourcePausePointResolveFailureReason.NoSequencePointOnOrAfterLine,
                    "fake",
                    _spans.Select(span => new SourcePausePointNearbyCompiledMethod(span.Name, span.Start, span.End)).ToArray());
            }

            private FakeMethodSpan SpanWithSequencePointAtOrNull(int line)
            {
                string text = _compiledLines[line - 1].Trim();
                if (text.Length == 0 || text.StartsWith("//", StringComparison.Ordinal))
                {
                    return null;
                }

                return _spans.FirstOrDefault(span => line > span.Start && line <= span.End);
            }
        }
    }
}
