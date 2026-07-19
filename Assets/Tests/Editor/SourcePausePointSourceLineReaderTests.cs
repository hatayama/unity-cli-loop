using System.IO;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies SourcePausePointSourceLineReader reads the trimmed text of a specific source
    /// line from disk, and degrades to an empty string for missing files or out-of-range lines.
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

        [Test]
        public void ReadLineText_WhenLineExists_ReturnsTrimmedText()
        {
            // Verifies the requested 1-based line is read and surrounding whitespace is trimmed.
            string result = SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 2);

            Assert.That(result, Is.EqualTo("line two"));
        }

        [Test]
        public void ReadLineText_WhenLineNumberExceedsFileLength_ReturnsEmpty()
        {
            // Verifies a line number past the end of the file degrades to an empty string instead of throwing.
            string result = SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 999);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ReadLineText_WhenFileDoesNotExist_ReturnsEmpty()
        {
            // Verifies a missing file degrades to an empty string instead of throwing.
            string result = SourcePausePointSourceLineReader.ReadLineText("/nonexistent/path/does-not-exist.cs", 1);

            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ReadLineText_WhenLineNumberIsZeroOrNegative_ReturnsEmpty()
        {
            // Verifies non-positive line numbers (invalid 1-based input) degrade to an empty string.
            Assert.That(SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, 0), Is.Empty);
            Assert.That(SourcePausePointSourceLineReader.ReadLineText(_tempFilePath, -1), Is.Empty);
        }
    }
}
