using System.Collections.Generic;
using System.Diagnostics;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the PAUSE_POINT_LINE_NOT_COMPILED refusals for an edited --line that has no
    /// compiled statement to arm. Every refusal but a line past the end of the file picks its
    /// next action from what the latest hot reload of the file did, so the caller gets a single
    /// instruction that can change the answer whichever of those cases applies.
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

        internal static PausePointResponse NoCompiledLineAtOrAfter(
            string file,
            int requestedLine,
            PausePointHotReloadFileState fileState)
        {
            AssertRequest(file, requestedLine);
            return Create(
                string.Format(
                    SourcePausePointConstants.LineNotCompiledNoCompiledLineAtOrAfterMessageFormat,
                    requestedLine,
                    file),
                fileState);
        }

        internal static PausePointResponse ChangedLine(
            string file,
            int requestedLine,
            string lineText,
            PausePointHotReloadFileState fileState)
        {
            AssertRequest(file, requestedLine);
            return Create(
                string.Format(
                    SourcePausePointConstants.LineNotCompiledChangedLineMessageFormat,
                    requestedLine,
                    file,
                    TrimOrEmpty(lineText)),
                fileState);
        }

        internal static PausePointResponse NextStatementUncompiled(
            string file,
            int requestedLine,
            int statementLine,
            string statementText,
            PausePointHotReloadFileState fileState)
        {
            AssertRequest(file, requestedLine);
            return Create(
                string.Format(
                    SourcePausePointConstants.LineNotCompiledNextStatementUncompiledMessageFormat,
                    requestedLine,
                    file,
                    statementLine,
                    TrimOrEmpty(statementText)),
                fileState);
        }

        internal static PausePointResponse StatementRemoved(
            string file,
            int requestedLine,
            string compiledStatementText,
            PausePointHotReloadFileState fileState)
        {
            AssertRequest(file, requestedLine);
            return Create(
                string.Format(
                    SourcePausePointConstants.LineNotCompiledStatementRemovedMessageFormat,
                    requestedLine,
                    file,
                    TrimOrEmpty(compiledStatementText)),
                fileState);
        }

        // Why the latest reload decides the next action: once it read the file as it is, hot
        // reloading the same contents again gives the same result, so only a change a Reason
        // names, or a compile, can make this line compiled.
        private static PausePointResponse Create(string message, PausePointHotReloadFileState fileState)
        {
            Debug.Assert(fileState != null, "fileState must not be null.");
            if (!fileState.LatestReloadReadFileAsItIs)
            {
                return PausePointFailureResponse.Create(
                    message,
                    SourcePausePointConstants.ErrorCodePausePointLineNotCompiled,
                    SourcePausePointConstants.LineNotCompiledRecommendedNextAction);
            }

            if (fileState.UnappliedRows.Count == 0)
            {
                return PausePointFailureResponse.Create(
                    message + SourcePausePointConstants.LineNotCompiledLatestReloadAppliedAllSuffix,
                    SourcePausePointConstants.ErrorCodePausePointLineNotCompiled,
                    SourcePausePointConstants.LineNotCompiledLatestReloadAppliedAllRecommendedNextAction);
            }

            return PausePointFailureResponse.Create(
                message + string.Format(
                    SourcePausePointConstants.LineNotCompiledLatestReloadLeftRowsSuffixFormat,
                    DescribeRows(fileState.UnappliedRows)),
                SourcePausePointConstants.ErrorCodePausePointLineNotCompiled,
                SourcePausePointConstants.LineNotCompiledLatestReloadLeftRowsRecommendedNextAction);
        }

        // Why every row, in reported order: the caller finds each one in the hot reload response
        // by its Methods[].Method string, and a cut list could drop the row that holds the line.
        private static string DescribeRows(IReadOnlyList<HotReloadUnappliedRow> rows)
        {
            List<string> described = new List<string>(rows.Count);
            foreach (HotReloadUnappliedRow row in rows)
            {
                string outcome = row.Kind == HotReloadUnappliedRowKind.Skipped
                    ? SourcePausePointConstants.HotReloadLeftBehindSkippedVerb
                    : SourcePausePointConstants.HotReloadLeftBehindFailedVerb;
                described.Add("'" + row.Label + "' (" + outcome + ")");
            }

            return string.Join(", ", described);
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
