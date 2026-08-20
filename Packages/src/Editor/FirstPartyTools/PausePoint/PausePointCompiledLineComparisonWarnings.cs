using System;
using System.Collections.Generic;
using System.IO;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds compiled-vs-edited line-drift and requested-line snap warnings for pause-point enable.
    /// </summary>
    internal static class PausePointCompiledLineComparisonWarnings
    {
        // Why success-only: resolve failure leaves ResolvedMethod and ResolvedLineText empty,
        // so this wording would point at fields that are not on the response.
        // Why same resolvedLine on both sides: the resolver rounds empty/comment lines forward,
        // so comparing the requested line to the resolved line is a false drift.
        // Why readOk is distinct from empty text: a blank edited line is a real mismatch;
        // a failed read is not evidence of drift.
        internal static string BuildCompiledLineDriftWarningOrEmpty(
            string compiledLineText,
            string editedLineText,
            string file,
            int resolvedLine,
            bool editedLineReadOk)
        {
            if (string.IsNullOrEmpty(compiledLineText) || !editedLineReadOk)
            {
                return string.Empty;
            }

            string compiledTrimmed = compiledLineText.Trim();
            string editedTrimmed = editedLineText == null ? string.Empty : editedLineText.Trim();
            if (editedTrimmed.Length == 0)
            {
                return string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineMapBlankEditedLineDriftWarningFormat,
                    SourcePausePointPathNormalizer.ToForwardSlashes(file),
                    resolvedLine,
                    compiledTrimmed);
            }

            if (string.Equals(compiledTrimmed, editedTrimmed, StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                SourcePausePointPathNormalizer.ToForwardSlashes(file),
                resolvedLine,
                compiledTrimmed,
                editedTrimmed);
        }

        // Why resolvedLine != requestedLine only: the compiled resolver rounds empty and comment
        // lines forward, so this inequality is the snap and has no false positive on this path.
        internal static string BuildLineSnapDisclosureWarningOrEmpty(
            string file,
            int requestedLine,
            int resolvedLine,
            string resolvedMethod,
            bool requestedLineReadOk,
            string requestedLineEditedText)
        {
            if (requestedLine <= 0 || resolvedLine <= 0 || resolvedLine == requestedLine)
            {
                return string.Empty;
            }

            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(file);
            string methodDisplay = resolvedMethod ?? string.Empty;
            if (!requestedLineReadOk)
            {
                return string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineSnapDisclosureWithoutEditedTextFormat,
                    normalizedFile,
                    requestedLine,
                    resolvedLine,
                    methodDisplay);
            }

            string requestedTrimmed = requestedLineEditedText == null
                ? string.Empty
                : requestedLineEditedText.Trim();
            if (requestedTrimmed.Length == 0)
            {
                return string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineSnapDisclosureBlankRequestedLineFormat,
                    normalizedFile,
                    requestedLine,
                    resolvedLine,
                    methodDisplay);
            }

            return string.Format(
                SourcePausePointConstants.HotReloadCompiledLineSnapDisclosureFormat,
                normalizedFile,
                requestedLine,
                requestedTrimmed,
                resolvedLine,
                methodDisplay);
        }

        // Why snap before resolved-line drift: the requested line is what the agent passed;
        // the armed line is what actually paused.
        // Why search requested-line text too: after a snap the armed line is often blank or a
        // brace, so the intended statement lives on the requested line.
        // Why skip the second candidate search when texts match: the suffix would duplicate.
        internal static string ComposeCompiledLineDriftAndSnapWarningOrEmpty(
            string file,
            int requestedLine,
            int resolvedLine,
            string resolvedMethod,
            string compiledResolvedLineText,
            bool resolvedEditedLineReadOk,
            string resolvedEditedLineText,
            bool requestedEditedLineReadOk,
            string requestedEditedLineText,
            int compiledMethodStartLine,
            int compiledMethodEndLine,
            IReadOnlyList<string> compiledSourceLines)
        {
            string snapWarning = BuildLineSnapDisclosureWarningOrEmpty(
                file,
                requestedLine,
                resolvedLine,
                resolvedMethod,
                requestedEditedLineReadOk,
                requestedEditedLineText);
            string driftWarning = BuildCompiledLineDriftWarningOrEmpty(
                compiledResolvedLineText,
                resolvedEditedLineText,
                file,
                resolvedLine,
                resolvedEditedLineReadOk);
            string combined = PausePointEnableWarnings.MergeWarnings(snapWarning, driftWarning);
            combined = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                combined,
                resolvedMethod,
                compiledMethodStartLine,
                compiledMethodEndLine);
            combined = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                combined,
                resolvedEditedLineText,
                compiledSourceLines);
            string resolvedTrimmed = resolvedEditedLineText == null
                ? string.Empty
                : resolvedEditedLineText.Trim();
            string requestedTrimmed = requestedEditedLineText == null
                ? string.Empty
                : requestedEditedLineText.Trim();
            if (!string.Equals(resolvedTrimmed, requestedTrimmed, StringComparison.Ordinal))
            {
                combined = PausePointEnableWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                    combined,
                    requestedEditedLineText,
                    compiledSourceLines);
            }

            return combined;
        }

        // Why not ReadLineTextFromSource: that helper returns empty for both a missing line
        // and a blank line, which used to suppress a real blank-vs-compiled mismatch.
        internal static (bool readOk, string text) ReadEditedLineText(string requestedFile, int lineNumber)
        {
            if (string.IsNullOrEmpty(requestedFile) || lineNumber <= 0)
            {
                return (false, string.Empty);
            }

            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(requestedFile);
            string absoluteFilePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), normalizedFile);
            if (!File.Exists(absoluteFilePath))
            {
                return (false, string.Empty);
            }

            string[] lines = SourcePausePointSourceLineReader.SplitSourceLines(File.ReadAllText(absoluteFilePath));
            if (lineNumber > lines.Length)
            {
                return (false, string.Empty);
            }

            return (true, lines[lineNumber - 1].Trim());
        }
    }
}
