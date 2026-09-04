#nullable enable
using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Verifies output-directory constants stay separator-free so Path.Combine supplies the platform separator.
    /// </summary>
    [TestFixture]
    public sealed class OutputDirectoryConstantsTests
    {
        private const string NoEmbeddedSeparatorMessage =
            "Segments must not embed a separator; Path.Combine supplies the platform separator on Windows.";
        private const string ConstantsSourceRelativePath =
            "Packages/src/Editor/ToolContracts/UnityCliLoopConstants.cs";
        private const string FileOutputDirectoriesMarker = "// File output directories";
        private const string OutputRootDirCombineDeclarationPattern =
            @"static\s+readonly\s+string\s+OUTPUT_ROOT_DIR\s*=\s*Path\.Combine\(";
        private const string StringLiteralPattern = "\"([^\"\\\\]|\\\\.)*\"";
        private const string SourceDeclarationRequiredMessage =
            "OUTPUT_ROOT_DIR must be declared as Path.Combine in source. Runtime equality cannot catch a revert to \".uloop/outputs\" on macOS because Path.Combine(\".uloop\", \"outputs\") yields the same string.";
        private const string SourceLiteralSeparatorMessage =
            "File output directory string literals must not embed a separator; Path.Combine supplies the platform separator on Windows.";

        /// <summary>
        /// Verifies OUTPUT_ROOT_DIR equals Path.Combine of ULOOP_DIR and OUTPUTS_DIR.
        /// </summary>
        [Test]
        public void OutputRootDir_IsBuiltFromSeparatorFreeSegments()
        {
            string expected = Path.Combine(UnityCliLoopConstants.ULOOP_DIR, UnityCliLoopConstants.OUTPUTS_DIR);
            Assert.That(UnityCliLoopConstants.OUTPUT_ROOT_DIR, Is.EqualTo(expected));
        }

        /// <summary>
        /// Verifies each output-directory segment constant contains neither '/' nor '\'.
        /// </summary>
        [Test]
        public void OutputDirectorySegments_ContainNoPathSeparators()
        {
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.ULOOP_DIR), UnityCliLoopConstants.ULOOP_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.OUTPUTS_DIR), UnityCliLoopConstants.OUTPUTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.TEST_RESULTS_DIR), UnityCliLoopConstants.TEST_RESULTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.HIERARCHY_RESULTS_DIR), UnityCliLoopConstants.HIERARCHY_RESULTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.FIND_GAMEOBJECTS_RESULTS_DIR), UnityCliLoopConstants.FIND_GAMEOBJECTS_RESULTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.SCREENSHOTS_DIR), UnityCliLoopConstants.SCREENSHOTS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(UnityCliLoopConstants.VIBE_LOGS_DIR), UnityCliLoopConstants.VIBE_LOGS_DIR);
            AssertSegmentHasNoPathSeparator(nameof(RecordInputConstants.INPUT_RECORDINGS_DIR), RecordInputConstants.INPUT_RECORDINGS_DIR);
        }

        /// <summary>
        /// Verifies OUTPUT_ROOT_DIR is declared as Path.Combine in source and that File output
        /// directory string literals embed no separator. Runtime checks cannot distinguish a
        /// literal ".uloop/outputs" on macOS because Path.Combine(".uloop", "outputs") is identical.
        /// </summary>
        [Test]
        public void OutputRootDir_IsDeclaredWithPathCombineInSource()
        {
            string projectRoot = UnityCliLoopPathResolver.GetProjectRoot();
            string sourcePath = Path.Combine(projectRoot, ConstantsSourceRelativePath);
            string source = File.ReadAllText(sourcePath);

            Assert.That(
                Regex.IsMatch(source, OutputRootDirCombineDeclarationPattern),
                Is.True,
                SourceDeclarationRequiredMessage);

            string block = ReadFileOutputDirectoriesBlock(source);
            string codeOnly = StripLineComments(block);
            MatchCollection literals = Regex.Matches(codeOnly, StringLiteralPattern);
            for (int index = 0; index < literals.Count; index++)
            {
                string literal = literals[index].Value;
                Assert.That(literal, Does.Not.Contain("/"), SourceLiteralSeparatorMessage + " (" + literal + ")");
                Assert.That(literal, Does.Not.Contain("\\"), SourceLiteralSeparatorMessage + " (" + literal + ")");
            }
        }

        private static string ReadFileOutputDirectoriesBlock(string source)
        {
            int startIndex = source.IndexOf(FileOutputDirectoriesMarker, StringComparison.Ordinal);
            Assert.That(startIndex, Is.GreaterThanOrEqualTo(0), "File output directories block is missing.");

            int windowsBlankLineIndex = source.IndexOf("\r\n\r\n", startIndex, StringComparison.Ordinal);
            int unixBlankLineIndex = source.IndexOf("\n\n", startIndex, StringComparison.Ordinal);
            int endIndex = FirstPositiveIndex(windowsBlankLineIndex, unixBlankLineIndex);
            Assert.That(endIndex, Is.GreaterThan(startIndex), "File output directories block has no trailing blank line.");
            return source.Substring(startIndex, endIndex - startIndex);
        }

        private static int FirstPositiveIndex(int firstIndex, int secondIndex)
        {
            if (firstIndex >= 0 && (secondIndex < 0 || firstIndex <= secondIndex))
            {
                return firstIndex;
            }

            return secondIndex;
        }

        private static string StripLineComments(string block)
        {
            string[] lines = block.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);
            string[] stripped = new string[lines.Length];
            for (int index = 0; index < lines.Length; index++)
            {
                string line = lines[index];
                int commentIndex = line.IndexOf("//", StringComparison.Ordinal);
                stripped[index] = commentIndex >= 0 ? line.Substring(0, commentIndex) : line;
            }

            return string.Join("\n", stripped);
        }

        private static void AssertSegmentHasNoPathSeparator(string name, string value)
        {
            Assert.That(value, Does.Not.Contain("/"), NoEmbeddedSeparatorMessage + " (" + name + ")");
            Assert.That(value, Does.Not.Contain("\\"), NoEmbeddedSeparatorMessage + " (" + name + ")");
        }
    }
}
