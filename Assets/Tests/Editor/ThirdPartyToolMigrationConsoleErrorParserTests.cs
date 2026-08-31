using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies that raw Unity Console error text is parsed back into structured
    /// CompileErrorLogEntry values, including Windows-style paths (backslashes, drive letters,
    /// spaces) that must parse correctly regardless of the host OS running the test.
    /// </summary>
    public sealed class ThirdPartyToolMigrationConsoleErrorParserTests
    {
        private const string ProjectRoot = "/Users/dev/Project";

        [Test]
        public void Parse_WithProjectRelativeUnixPath_ReturnsEntryWithAbsolutePath()
        {
            // Verifies a standard csc/Roslyn diagnostic line with a project-relative Unix-style path
            // is parsed into an absolute file path, line number, and code-prefixed message.
            List<string> rawMessages = new List<string>
            {
                "Assets/Editor/Foo.cs(3,25): error CS0246: The type or namespace name 'Bar' could not be found"
            };

            List<CompileErrorLogEntry> entries = ThirdPartyToolMigrationConsoleErrorParser.Parse(
                rawMessages,
                ProjectRoot);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].FilePath, Is.EqualTo("/Users/dev/Project/Assets/Editor/Foo.cs"));
            Assert.That(entries[0].LineNumber, Is.EqualTo(3));
            Assert.That(entries[0].Message, Does.Contain("Bar"));
        }

        [Test]
        public void Parse_WithBackslashSeparatedRelativePath_NormalizesToForwardSlashAbsolutePath()
        {
            // Verifies Windows-style backslash separators in a project-relative path are normalized
            // to forward slashes and combined with the project root, without relying on the host OS's
            // own path-separator handling.
            List<string> rawMessages = new List<string>
            {
                @"Assets\Editor\Foo.cs(3,25): error CS0246: The type or namespace name 'Bar' could not be found"
            };

            List<CompileErrorLogEntry> entries = ThirdPartyToolMigrationConsoleErrorParser.Parse(
                rawMessages,
                ProjectRoot);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].FilePath, Is.EqualTo("/Users/dev/Project/Assets/Editor/Foo.cs"));
        }

        [Test]
        public void Parse_WithWindowsDriveLetterAbsolutePath_KeepsPathAbsoluteWithoutProjectRoot()
        {
            // Verifies a Windows drive-letter absolute path is recognized as already-rooted (via an
            // explicit drive-letter check, not Path.IsPathRooted, which is host-OS dependent) and is
            // not incorrectly combined with the project root.
            List<string> rawMessages = new List<string>
            {
                @"C:\Users\dev\Project\Assets\Editor\Foo.cs(10,1): error CS0246: The type or namespace name 'Bar' could not be found"
            };

            List<CompileErrorLogEntry> entries = ThirdPartyToolMigrationConsoleErrorParser.Parse(
                rawMessages,
                ProjectRoot);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].FilePath, Is.EqualTo("C:/Users/dev/Project/Assets/Editor/Foo.cs"));
        }

        [Test]
        public void Parse_WithSpacesInPath_ParsesFullPathIncludingSpaces()
        {
            // Verifies a path containing spaces (e.g. a "My Tools" folder) is parsed in full, not
            // truncated at the space.
            List<string> rawMessages = new List<string>
            {
                "Assets/My Tools/Foo.cs(5,10): error CS0246: The type or namespace name 'Bar' could not be found"
            };

            List<CompileErrorLogEntry> entries = ThirdPartyToolMigrationConsoleErrorParser.Parse(
                rawMessages,
                ProjectRoot);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].FilePath, Is.EqualTo("/Users/dev/Project/Assets/My Tools/Foo.cs"));
        }

        [Test]
        public void Parse_WithWarningSeverity_IsSkipped()
        {
            // Verifies warning-severity diagnostic lines are not treated as compile errors.
            List<string> rawMessages = new List<string>
            {
                "Assets/Editor/Foo.cs(3,25): warning CS0219: The variable 'x' is never used"
            };

            List<CompileErrorLogEntry> entries = ThirdPartyToolMigrationConsoleErrorParser.Parse(
                rawMessages,
                ProjectRoot);

            Assert.That(entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void Parse_WithNonDiagnosticFormatLine_IsSkippedWithoutThrowing()
        {
            // Verifies plain (non-compiler-diagnostic) console messages are silently ignored.
            List<string> rawMessages = new List<string>
            {
                "This is a regular Debug.LogError message with no file/line prefix"
            };

            List<CompileErrorLogEntry> entries = ThirdPartyToolMigrationConsoleErrorParser.Parse(
                rawMessages,
                ProjectRoot);

            Assert.That(entries.Count, Is.EqualTo(0));
        }

        [Test]
        public void Parse_WithMultiLineMessage_UsesOnlyFirstLine()
        {
            // Verifies only the first physical line of a multi-line console message is parsed for the
            // diagnostic file/line prefix.
            List<string> rawMessages = new List<string>
            {
                "Assets/Editor/Foo.cs(3,25): error CS0246: The type or namespace name 'Bar' could not be found\nSome trailing detail line"
            };

            List<CompileErrorLogEntry> entries = ThirdPartyToolMigrationConsoleErrorParser.Parse(
                rawMessages,
                ProjectRoot);

            Assert.That(entries.Count, Is.EqualTo(1));
            Assert.That(entries[0].FilePath, Is.EqualTo("/Users/dev/Project/Assets/Editor/Foo.cs"));
        }
    }
}
