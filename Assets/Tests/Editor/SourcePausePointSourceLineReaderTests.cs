using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies SourcePausePointSourceLineReader reads trimmed source text from disk, joining a
    /// StartLine..EndLine span onto one line, and degrades to an empty string for missing files
    /// or out-of-range start lines.
    /// </summary>
    [TestFixture]
    public sealed class SourcePausePointSourceLineReaderTests
    {
        private string _tempFilePath;

        [SetUp]
        public void SetUp()
        {
            _tempFilePath = Path.GetTempFileName();
            File.WriteAllLines(_tempFilePath, new[] { "line one", "    line two   ", "line three" });
        }

        [TearDown]
        public void TearDown()
        {
            File.Delete(_tempFilePath);
        }

        /// <summary>
        /// Verifies the requested 1-based line is read and surrounding whitespace is trimmed.
        /// </summary>
        [Test]
        public void ReadLineText_WhenLineExists_ReturnsTrimmedText()
        {
            string result = SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 2, 2);

            Assert.That(result, Is.EqualTo("line two"));
        }

        /// <summary>
        /// Verifies a line number past the end of the file degrades to an empty string instead of throwing.
        /// </summary>
        [Test]
        public void ReadLineText_WhenLineNumberExceedsFileLength_ReturnsEmpty()
        {
            string result = SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 999, 999);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies a missing file degrades to an empty string instead of throwing.
        /// </summary>
        [Test]
        public void ReadLineText_WhenFileDoesNotExist_ReturnsEmpty()
        {
            string result = SourcePausePointSourceLineReader.ReadLineText("/nonexistent/path/does-not-exist.cs", 1, 1);

            Assert.That(result, Is.Empty);
        }

        /// <summary>
        /// Verifies non-positive line numbers (invalid 1-based input) degrade to an empty string.
        /// </summary>
        [Test]
        public void ReadLineText_WhenLineNumberIsZeroOrNegative_ReturnsEmpty()
        {
            Assert.That(SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 0, 0), Is.Empty);
            Assert.That(SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, -1, -1), Is.Empty);
        }

        /// <summary>
        /// Verifies a multi-line span is Trim'd per physical line, empty lines dropped, and joined with one space.
        /// </summary>
        [Test]
        public void ReadLineText_WhenSpanCoversMultipleLines_JoinsTrimmedNonEmptyLinesWithSingleSpaces()
        {
            File.WriteAllLines(_tempFilePath, new[]
            {
                "Debug.Assert(",
                "    value > 0,",
                "",
                "    \"value must be positive\");"
            });

            string result = SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 1, 4);

            Assert.That(result, Is.EqualTo("Debug.Assert( value > 0, \"value must be positive\");"));
        }

        /// <summary>
        /// Verifies a single-line span still returns only that line's trimmed text.
        /// </summary>
        [Test]
        public void ReadLineText_WhenStartAndEndAreTheSameLine_ReturnsThatTrimmedLine()
        {
            string result = SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 2, 2);

            Assert.That(result, Is.EqualTo("line two"));
        }

        /// <summary>
        /// Verifies EndLine &lt; StartLine or EndLine &lt;= 0 falls back to reading StartLine alone.
        /// </summary>
        [Test]
        public void ReadLineText_WhenEndLineIsBeforeStartLineOrNonPositive_FallsBackToStartLineOnly()
        {
            Assert.That(SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 2, 1), Is.EqualTo("line two"));
            Assert.That(SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 2, 0), Is.EqualTo("line two"));
            Assert.That(SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 2, -3), Is.EqualTo("line two"));
        }

        /// <summary>
        /// Verifies an EndLine past EOF is truncated to the last existing line instead of throwing.
        /// </summary>
        [Test]
        public void ReadLineText_WhenEndLineExceedsFileLength_JoinsThroughLastExistingLine()
        {
            string result = SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 2, 999);

            Assert.That(result, Is.EqualTo("line two line three"));
        }
    }
}
