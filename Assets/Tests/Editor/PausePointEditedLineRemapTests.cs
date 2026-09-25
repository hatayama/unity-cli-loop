using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies edited-line remap onto a named method's compiled span.
    /// </summary>
    [TestFixture]
    public sealed class PausePointEditedLineRemapTests
    {
        private const string ExpectedRemapWarning =
            "--line 16 in method 'UniqueTarget' was matched by its text to line 10 in the last compiled source, so the marker was placed at line 10, not at line 16. Verify ResolvedLocation, or run 'uloop compile' and re-enable to use edited-file line numbers.";

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
    }
}
