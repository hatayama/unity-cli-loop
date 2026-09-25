using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the PAUSE_POINT_LINE_NOT_COMPILED refusals for an edited --line that has no
    /// compiled statement to arm. Every refusal but a line past the end of the file shares one
    /// next action, so the caller gets a single instruction whichever of those cases applies.
    /// </summary>
    internal static class PausePointLineNotCompiledRefusal
    {
        internal static PausePointResponse BeyondEndOfFile(string file, int requestedLine, int editedLineCount)
        {
            AssertRequest(file, requestedLine);
            return PausePointFailureResponse.Create(
                string.Format(
                    SourcePausePointConstants.LineNotCompiledBeyondEndOfFileMessageFormat,
                    requestedLine,
                    file,
                    editedLineCount),
                SourcePausePointConstants.ErrorCodePausePointLineNotCompiled,
                string.Format(
                    SourcePausePointConstants.LineNotCompiledBeyondEndOfFileRecommendedNextActionFormat,
                    editedLineCount));
        }

        internal static PausePointResponse NoCompiledLineAtOrAfter(string file, int requestedLine)
        {
            AssertRequest(file, requestedLine);
            return Create(string.Format(
                SourcePausePointConstants.LineNotCompiledNoCompiledLineAtOrAfterMessageFormat,
                requestedLine,
                file));
        }

        internal static PausePointResponse ChangedLine(string file, int requestedLine, string lineText)
        {
            AssertRequest(file, requestedLine);
            return Create(string.Format(
                SourcePausePointConstants.LineNotCompiledChangedLineMessageFormat,
                requestedLine,
                file,
                TrimOrEmpty(lineText)));
        }

        internal static PausePointResponse NextStatementUncompiled(
            string file,
            int requestedLine,
            int statementLine,
            string statementText)
        {
            AssertRequest(file, requestedLine);
            return Create(string.Format(
                SourcePausePointConstants.LineNotCompiledNextStatementUncompiledMessageFormat,
                requestedLine,
                file,
                statementLine,
                TrimOrEmpty(statementText)));
        }

        internal static PausePointResponse StatementRemoved(string file, int requestedLine, string compiledStatementText)
        {
            AssertRequest(file, requestedLine);
            return Create(string.Format(
                SourcePausePointConstants.LineNotCompiledStatementRemovedMessageFormat,
                requestedLine,
                file,
                TrimOrEmpty(compiledStatementText)));
        }

        private static PausePointResponse Create(string message)
        {
            return PausePointFailureResponse.Create(
                message,
                SourcePausePointConstants.ErrorCodePausePointLineNotCompiled,
                SourcePausePointConstants.LineNotCompiledRecommendedNextAction);
        }

        private static void AssertRequest(string file, int requestedLine)
        {
            Debug.Assert(!string.IsNullOrEmpty(file), "file must not be null or empty.");
            Debug.Assert(requestedLine > 0, "requestedLine must be a positive 1-based line number.");
        }

        private static string TrimOrEmpty(string text)
        {
            return text == null ? string.Empty : text.Trim();
        }
    }
}
