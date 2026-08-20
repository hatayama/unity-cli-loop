using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Tests best-effort File/Line parsing from failed-test stack traces.
    /// </summary>
    public sealed class FailedTestStackLocationParserTests
    {
        /// <summary>
        /// What: "(at path:line)" yields that file and line.
        /// </summary>
        [Test]
        public void TryParse_WhenParenthesizedAtPathLine_ReturnsFileAndLine()
        {
            (string file, int? line) = FailedTestStackLocationParser.TryParse(
                "  (at Assets/Tests/FailingTest.cs:42)");

            Assert.That(file, Is.EqualTo("Assets/Tests/FailingTest.cs"));
            Assert.That(line, Is.EqualTo(42));
        }

        /// <summary>
        /// What: Unity Test Runner "in path:line" traces still yield File and Line.
        /// </summary>
        [Test]
        public void TryParse_WhenUnityInPathLine_ReturnsFileAndLine()
        {
            (string file, int? line) = FailedTestStackLocationParser.TryParse(
                "at Example.Tests.FailingTest () [0x00000] in Assets/Tests/FailingTest.cs:42");

            Assert.That(file, Is.EqualTo("Assets/Tests/FailingTest.cs"));
            Assert.That(line, Is.EqualTo(42));
        }

        /// <summary>
        /// What: an empty stack trace yields no location.
        /// </summary>
        [Test]
        public void TryParse_WhenStackTraceEmpty_ReturnsNullLocation()
        {
            (string file, int? line) = FailedTestStackLocationParser.TryParse(string.Empty);

            Assert.That(file, Is.Null);
            Assert.That(line, Is.Null);
        }

        /// <summary>
        /// What: a stack without a path:line location yields no File or Line.
        /// </summary>
        [Test]
        public void TryParse_WhenStackHasNoPathLine_ReturnsNullLocation()
        {
            (string file, int? line) = FailedTestStackLocationParser.TryParse(
                "AssertionException: Expected 2 But was: 1");

            Assert.That(file, Is.Null);
            Assert.That(line, Is.Null);
        }

        /// <summary>
        /// What: a 20-digit line number yields no location instead of overflowing int.Parse
        /// or reporting a truncated 9-digit line from an unanchored match.
        /// </summary>
        [Test]
        public void TryParse_WhenLineNumberHasTwentyDigits_ReturnsNullLocation()
        {
            (string parenthesizedFile, int? parenthesizedLine) = FailedTestStackLocationParser.TryParse(
                "  (at Assets/Tests/FailingTest.cs:12345678901234567890)");
            Assert.That(parenthesizedFile, Is.Null);
            Assert.That(parenthesizedLine, Is.Null);

            (string inFile, int? inLine) = FailedTestStackLocationParser.TryParse(
                "at Example.Tests.FailingTest () [0x00000] in Assets/Tests/FailingTest.cs:12345678901234567890");
            Assert.That(inFile, Is.Null);
            Assert.That(inLine, Is.Null);
        }
    }
}
