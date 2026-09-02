using NUnit.Framework;

using io.github.hatayama.UnityCliLoop.FirstPartyTools;

namespace io.github.hatayama.UnityCliLoop.Tests.Editor
{
    /// <summary>
    /// Pins the Console error summary appended to indeterminate compile results so agents can
    /// self-correct without a separate get-logs round trip.
    /// </summary>
    [TestFixture]
    public sealed class CompileIndeterminateErrorSummaryBuilderTests
    {
        private const string DuplicateReferencesError =
            "Assembly has duplicate references: UnityEngine.TestRunner,UnityEditor.TestRunner";

        /// <summary>
        /// What: an empty Console snapshot produces no summary so the base message stays untouched.
        /// </summary>
        [Test]
        public void Build_WhenNoEntries_ReturnsNull()
        {
            string summary = CompileIndeterminateErrorSummaryBuilder.Build(new UnityCliLoopConsoleLogEntry[0]);

            Assert.That(summary, Is.Null);
        }

        /// <summary>
        /// What: entries that are not errors never appear in the summary.
        /// </summary>
        [Test]
        public void Build_WhenOnlyNonErrorEntries_ReturnsNull()
        {
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(UnityCliLoopLogType.Warning, "some warning", string.Empty),
                new(UnityCliLoopLogType.Log, "some log", string.Empty)
            };

            string summary = CompileIndeterminateErrorSummaryBuilder.Build(entries);

            Assert.That(summary, Is.Null);
        }

        /// <summary>
        /// What: an error entry is listed under the summary header using only its first line.
        /// </summary>
        [Test]
        public void Build_WhenErrorEntryExists_ListsFirstLineUnderHeader()
        {
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(UnityCliLoopLogType.Error, DuplicateReferencesError + "\nsecond line", "stack")
            };

            string summary = CompileIndeterminateErrorSummaryBuilder.Build(entries);

            Assert.That(
                summary,
                Is.EqualTo(
                    CompileIndeterminateErrorSummaryBuilder.Header + "\n- " + DuplicateReferencesError));
        }

        /// <summary>
        /// What: identical error messages are listed once.
        /// </summary>
        [Test]
        public void Build_WhenDuplicateErrors_ListsThemOnce()
        {
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(UnityCliLoopLogType.Error, DuplicateReferencesError, string.Empty),
                new(UnityCliLoopLogType.Error, DuplicateReferencesError, string.Empty)
            };

            string summary = CompileIndeterminateErrorSummaryBuilder.Build(entries);

            Assert.That(
                summary,
                Is.EqualTo(
                    CompileIndeterminateErrorSummaryBuilder.Header + "\n- " + DuplicateReferencesError));
        }

        /// <summary>
        /// What: only the most recent errors are kept, in Console order, when more exist than the cap.
        /// </summary>
        [Test]
        public void Build_WhenMoreErrorsThanCap_KeepsMostRecentInOrder()
        {
            int total = CompileIndeterminateErrorSummaryBuilder.MaxEntries + 2;
            UnityCliLoopConsoleLogEntry[] entries = new UnityCliLoopConsoleLogEntry[total];
            for (int index = 0; index < total; index++)
            {
                entries[index] = new UnityCliLoopConsoleLogEntry(UnityCliLoopLogType.Error, $"error {index}", string.Empty);
            }

            string summary = CompileIndeterminateErrorSummaryBuilder.Build(entries);

            string[] lines = summary.Split('\n');
            Assert.That(lines.Length, Is.EqualTo(CompileIndeterminateErrorSummaryBuilder.MaxEntries + 1));
            Assert.That(lines[1], Is.EqualTo("- error 2"));
            Assert.That(lines[lines.Length - 1], Is.EqualTo($"- error {total - 1}"));
        }

        /// <summary>
        /// What: a very long error line is truncated with an ellipsis so the response stays short.
        /// </summary>
        [Test]
        public void Build_WhenErrorLineIsTooLong_TruncatesWithEllipsis()
        {
            string longMessage = new string('x', CompileIndeterminateErrorSummaryBuilder.MaxLineLength + 50);
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(UnityCliLoopLogType.Error, longMessage, string.Empty)
            };

            string summary = CompileIndeterminateErrorSummaryBuilder.Build(entries);

            string line = summary.Split('\n')[1];
            Assert.That(line, Does.EndWith("..."));
            Assert.That(line.Length, Is.EqualTo(CompileIndeterminateErrorSummaryBuilder.MaxLineLength + 2 + 3));
        }

        /// <summary>
        /// What: blank error messages are skipped instead of producing empty bullet lines.
        /// </summary>
        [Test]
        public void Build_WhenErrorMessageIsBlank_SkipsEntry()
        {
            UnityCliLoopConsoleLogEntry[] entries =
            {
                new(UnityCliLoopLogType.Error, "   ", string.Empty),
                new(UnityCliLoopLogType.Error, null, string.Empty)
            };

            string summary = CompileIndeterminateErrorSummaryBuilder.Build(entries);

            Assert.That(summary, Is.Null);
        }
    }
}
