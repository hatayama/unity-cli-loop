using System;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.HotReload
{
    /// <summary>
    /// Verifies unresolved-member extraction and skipped-member notes for shim compile failures.
    /// </summary>
    public sealed class HotReloadSkippedMemberCompileNoteTests
    {
        private const string Surface11Cs1061 =
            "CS1061: 'TetrisGameController' does not contain a definition for 'DescribeValue' and no accessible extension method 'DescribeValue' accepting a first argument of type 'TetrisGameController' could be found (are you missing a using directive or an assembly reference?) (line 95)";

        private const string Surface11SkippedMethod =
            "Tetris.Presentation.TetrisGameController.DescribeValue`1(T)";

        private const string Surface11SkippedReason =
            "Added generic methods are skipped; hot reload cannot emit a typed shim for them. Run 'uloop compile'.";

        private const string ExpectedSkippedMemberNote =
            "'DescribeValue' was skipped by this hot reload run, which is why this compile failed: "
            + "Added generic methods are skipped; hot reload cannot emit a typed shim for them. Run 'uloop compile'.";

        /// <summary>
        /// What: the surface-11 CS1061 line yields the unresolved member name DescribeValue.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_Surface11Cs1061_ReturnsDescribeValue()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[] { Surface11Cs1061 });

            Assert.That(names, Is.EqualTo(new[] { "DescribeValue" }));
        }

        /// <summary>
        /// What: CS0117 uses the same definition-for quote as CS1061.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_Cs0117_ReturnsQuotedMemberName()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[]
                {
                    "CS0117: 'Host' does not contain a definition for 'MissingHelper'"
                });

            Assert.That(names, Is.EqualTo(new[] { "MissingHelper" }));
        }

        /// <summary>
        /// What: CS0103 yields the quoted name from The name 'X' does not exist.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_Cs0103_ReturnsQuotedName()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[]
                {
                    "CS0103: The name 'MissingHelperAddedByEdit' does not exist in the current context"
                });

            Assert.That(names, Is.EqualTo(new[] { "MissingHelperAddedByEdit" }));
        }

        /// <summary>
        /// What: duplicate unresolved names from several diagnostics are returned once, in first-seen order.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_DuplicateNames_ReturnsEachNameOnce()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[]
                {
                    Surface11Cs1061,
                    "CS0103: The name 'DescribeValue' does not exist in the current context",
                    "CS0117: 'Host' does not contain a definition for 'OtherMissing'"
                });

            Assert.That(names, Is.EqualTo(new[] { "DescribeValue", "OtherMissing" }));
        }

        /// <summary>
        /// What: lines that are not CS1061/CS0117/CS0103 prefixes are ignored.
        /// </summary>
        [Test]
        public void ExtractUnresolvedMemberNames_UnrelatedDiagnostic_ReturnsEmpty()
        {
            string[] names = HotReloadSkippedMemberCompileNote.ExtractUnresolvedMemberNames(
                new[]
                {
                    "CS0229: Ambiguity between 'A.DescribeValue' and 'B.DescribeValue'",
                    "hint: CS1061: 'Host' does not contain a definition for 'DescribeValue'"
                });

            Assert.That(names, Is.EqualTo(Array.Empty<string>()));
        }

        /// <summary>
        /// What: the surface-11 generic skip row matches DescribeValue and returns that reason.
        /// </summary>
        [Test]
        public void FindSkippedMemberNote_Surface11GenericSkip_ReturnsReason()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = Surface11SkippedMethod,
                    reason = Surface11SkippedReason
                }
            };

            string note = HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("DescribeValue", skipped);

            Assert.That(note, Is.EqualTo(Surface11SkippedReason));
        }

        /// <summary>
        /// What: a skipped method whose simple name does not match returns null.
        /// </summary>
        [Test]
        public void FindSkippedMemberNote_WhenNoSimpleNameMatches_ReturnsNull()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = Surface11SkippedMethod,
                    reason = Surface11SkippedReason
                }
            };

            string note = HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("BuildDiagnosticLine", skipped);

            Assert.That(note, Is.Null);
        }

        /// <summary>
        /// What: a skipped label whose parameter type contains dots still matches the simple name.
        /// </summary>
        [Test]
        public void FindSkippedMemberNote_WhenParameterTypeIsQualified_UsesMethodSimpleName()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = "Ns.Type.DescribeValue(System.Int32)",
                    reason = Surface11SkippedReason
                }
            };

            string note = HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("DescribeValue", skipped);

            Assert.That(note, Is.EqualTo(Surface11SkippedReason));
        }

        /// <summary>
        /// What: the skipped-member note format plus the surface-11 reason is an exact full line.
        /// </summary>
        [Test]
        public void SkippedMemberCompileFailureNote_Surface11_MatchesFullText()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = Surface11SkippedMethod,
                    reason = Surface11SkippedReason
                }
            };
            string reason = HotReloadSkippedMemberCompileNote.FindSkippedMemberNote("DescribeValue", skipped);
            string note = string.Format(
                HotReloadConstants.SkippedMemberCompileFailureNoteFormat,
                "DescribeValue",
                reason);

            Assert.That(note, Is.EqualTo(ExpectedSkippedMemberNote));
        }

        /// <summary>
        /// What: AppendNotes appends the skipped-member note after the composed shim-compile hints.
        /// </summary>
        [Test]
        public void AppendNotes_Surface11Cs1061_AppendsFullNoteAfterComposeHints()
        {
            TransformWorkerSkippedDto[] skipped =
            {
                new TransformWorkerSkippedDto
                {
                    method = Surface11SkippedMethod,
                    reason = Surface11SkippedReason
                }
            };
            string composed = HotReloadShimCompiler.ComposeShimCompileFailureMessage(
                new[] { Surface11Cs1061 });
            string expected = composed + "\n" + ExpectedSkippedMemberNote;

            string message = HotReloadSkippedMemberCompileNote.AppendNotes(
                composed,
                new[] { Surface11Cs1061 },
                skipped);

            Assert.That(message, Is.EqualTo(expected));
        }
    }
}
