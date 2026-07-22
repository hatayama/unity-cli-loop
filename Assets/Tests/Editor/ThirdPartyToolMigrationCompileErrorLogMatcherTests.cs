using System.Collections.Generic;

using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.Infrastructure;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Test fixture that verifies compile-error-log entries are matched against V2 legacy API tokens
    /// and reduced to a deduplicated set of migration target file paths.
    /// </summary>
    public sealed class ThirdPartyToolMigrationCompileErrorLogMatcherTests
    {
        [Test]
        public void Match_WhenNoEntryContainsLegacyToken_ReturnsEmptyResult()
        {
            // Verifies that unrelated V3 compile errors produce no matched entries and no target file paths.
            List<CompileErrorLogEntry> entries = new List<CompileErrorLogEntry>
            {
                new CompileErrorLogEntry(
                    "Assets/Editor/MyTool.cs(1,1): error CS0103: The name 'undefinedSymbol' does not exist in the current context",
                    "Assets/Editor/MyTool.cs",
                    1)
            };

            ThirdPartyToolMigrationCompileErrorLogMatchResult result =
                ThirdPartyToolMigrationCompileErrorLogMatcher.Match(entries);

            Assert.That(result.MatchedEntries, Is.Empty);
            Assert.That(result.TargetFilePaths, Is.Empty);
        }

        [Test]
        public void Match_WhenEntryContainsLegacyToken_ReturnsMatchedEntryAndTargetFilePath()
        {
            // Verifies that an entry whose message contains a legacy API token is included in both
            // the matched-entries list and the target file path list.
            List<CompileErrorLogEntry> entries = new List<CompileErrorLogEntry>
            {
                new CompileErrorLogEntry(
                    "Assets/Editor/MyTool.cs(3,25): error CS0234: The type or namespace name 'uLoopMCP' " +
                    "does not exist in the namespace 'io.github.hatayama' (are you missing an assembly reference?)",
                    "Assets/Editor/MyTool.cs",
                    3)
            };

            ThirdPartyToolMigrationCompileErrorLogMatchResult result =
                ThirdPartyToolMigrationCompileErrorLogMatcher.Match(entries);

            Assert.That(result.MatchedEntries.Count, Is.EqualTo(1));
            Assert.That(result.TargetFilePaths, Is.EqualTo(new[] { "Assets/Editor/MyTool.cs" }));
        }

        [Test]
        public void Match_WhenMultipleEntriesShareTheSameFilePath_DeduplicatesTargetFilePaths()
        {
            // Verifies that multiple legacy-token-matching errors in the same file collapse to one target path.
            List<CompileErrorLogEntry> entries = new List<CompileErrorLogEntry>
            {
                new CompileErrorLogEntry(
                    "Assets/Editor/MyTool.cs(3,25): error CS0234: The type or namespace name 'uLoopMCP' " +
                    "does not exist in the namespace 'io.github.hatayama' (are you missing an assembly reference?)",
                    "Assets/Editor/MyTool.cs",
                    3),
                new CompileErrorLogEntry(
                    "Assets/Editor/MyTool.cs(9,14): error CS0246: The type or namespace name 'AbstractUnityTool' " +
                    "could not be found (are you missing a using directive or an assembly reference?)",
                    "Assets/Editor/MyTool.cs",
                    9)
            };

            ThirdPartyToolMigrationCompileErrorLogMatchResult result =
                ThirdPartyToolMigrationCompileErrorLogMatcher.Match(entries);

            Assert.That(result.MatchedEntries.Count, Is.EqualTo(2));
            Assert.That(result.TargetFilePaths, Is.EqualTo(new[] { "Assets/Editor/MyTool.cs" }));
        }

        [Test]
        public void Match_WhenEntriesMixMatchingAndNonMatching_OnlyIncludesMatchingFilePaths()
        {
            // Verifies that a non-matching entry does not contribute its file path even when it is
            // interleaved with matching entries from other files.
            List<CompileErrorLogEntry> entries = new List<CompileErrorLogEntry>
            {
                new CompileErrorLogEntry(
                    "Assets/Editor/Unrelated.cs(1,1): error CS0103: The name 'undefinedSymbol' does not exist in the current context",
                    "Assets/Editor/Unrelated.cs",
                    1),
                new CompileErrorLogEntry(
                    "Assets/Editor/LegacyTool.cs(2,6): error CS0246: The type or namespace name 'McpTool' " +
                    "could not be found (are you missing a using directive or an assembly reference?)",
                    "Assets/Editor/LegacyTool.cs",
                    2)
            };

            ThirdPartyToolMigrationCompileErrorLogMatchResult result =
                ThirdPartyToolMigrationCompileErrorLogMatcher.Match(entries);

            Assert.That(result.MatchedEntries.Count, Is.EqualTo(1));
            Assert.That(result.TargetFilePaths, Is.EqualTo(new[] { "Assets/Editor/LegacyTool.cs" }));
        }

        [Test]
        public void Match_WhenEntriesIsNull_Throws()
        {
            // Verifies fail-fast behavior when the entry collection itself is missing.
            Assert.Throws<System.ArgumentNullException>(() =>
                ThirdPartyToolMigrationCompileErrorLogMatcher.Match(null));
        }
    }
}
