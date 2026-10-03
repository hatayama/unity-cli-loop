using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor.DynamicCodeToolTests
{
    /// <summary>
    /// Verifies how the user snippet is cut out of wrapped source when markers are missing or incomplete, and
    /// how its lines are normalized.
    /// </summary>
    public sealed class WrappedDynamicCodeUserSnippetExtractorTests
    {
        /// <summary>
        /// Verifies a start marker on the last line, with nothing after it, yields no snippet.
        /// </summary>
        [Test]
        public void TryExtract_WhenTheStartMarkerEndsTheSource_ReturnsFalse()
        {
            bool extracted = WrappedDynamicCodeUserSnippetExtractor.TryExtract(
                "class Wrapper {\n" + WrapperTemplate.UserCodeStartMarker,
                out string snippet);

            Assert.That(extracted, Is.False);
            Assert.That(snippet, Is.Empty);
        }

        /// <summary>
        /// Verifies a missing end marker takes everything after the start marker line.
        /// </summary>
        [Test]
        public void TryExtract_WithoutAnEndMarker_TakesTheRestOfTheSource()
        {
            bool extracted = WrappedDynamicCodeUserSnippetExtractor.TryExtract(
                "class Wrapper {\n" + WrapperTemplate.UserCodeStartMarker + "\nint x = 1;\nreturn x;",
                out string snippet);

            Assert.That(extracted, Is.True);
            Assert.That(snippet, Is.EqualTo("int x = 1;\nreturn x;"));
        }

        /// <summary>
        /// Verifies an empty snippet has no lines.
        /// </summary>
        [Test]
        public void SplitNormalizedLines_WithAnEmptySnippet_ReturnsNoLines()
        {
            Assert.That(WrappedDynamicCodeUserSnippetExtractor.SplitNormalizedLines(string.Empty), Is.Empty);
        }

        /// <summary>
        /// Verifies lines lose leading indentation and carriage returns, and trailing empty lines are dropped.
        /// </summary>
        [Test]
        public void SplitNormalizedLines_TrimsIndentationCarriageReturnsAndTrailingEmptyLines()
        {
            string[] lines = WrappedDynamicCodeUserSnippetExtractor.SplitNormalizedLines("    int x = 1;\r\n\treturn x;\r\n\r\n");

            Assert.That(lines, Is.EqualTo(new[] { "int x = 1;", "return x;" }));
        }
    }
}
