using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Appends "Candidate:" compiled-line hints to drift warnings and resolve-failure messages.
    /// </summary>
    internal static class PausePointCandidateCompiledLineWarnings
    {
        // Why only after a non-empty drift warning: a candidate list without drift would look
        // like a second resolution.
        // Why skip empty edited text: a blank line has no statement to locate in compiled source.
        internal static string AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
            string driftWarning,
            string editedLineText,
            IReadOnlyList<string> compiledSourceLines,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans = null)
        {
            if (string.IsNullOrEmpty(driftWarning))
            {
                return driftWarning ?? string.Empty;
            }

            if (string.IsNullOrEmpty(editedLineText) || compiledSourceLines == null)
            {
                return driftWarning;
            }

            string editedTrimmed = editedLineText.Trim();
            if (editedTrimmed.Length == 0)
            {
                return driftWarning;
            }

            (List<int> matches, bool truncated) = CollectCandidateCompiledLineNumbers(
                editedTrimmed,
                compiledSourceLines);
            if (matches.Count == 0)
            {
                return driftWarning;
            }

            return driftWarning + FormatCandidateCompiledLinesSuffix(
                matches,
                truncated,
                namedCompiledMethodSpans);
        }

        // Why a distinct sentence: the resolved-line candidate does not name --line, so two
        // identical "edited line" suffixes would not say which search produced which hit.
        internal static string AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged(
            string driftWarning,
            int requestedLine,
            string requestedLineEditedText,
            IReadOnlyList<string> compiledSourceLines,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans = null)
        {
            if (string.IsNullOrEmpty(driftWarning) || requestedLine <= 0)
            {
                return driftWarning ?? string.Empty;
            }

            if (string.IsNullOrEmpty(requestedLineEditedText) || compiledSourceLines == null)
            {
                return driftWarning;
            }

            string editedTrimmed = requestedLineEditedText.Trim();
            if (editedTrimmed.Length == 0)
            {
                return driftWarning;
            }

            // Why: a brace-only line matches almost every method, so its Candidate list points nowhere.
            if (PausePointCompiledLineComparisonWarnings.IsTrivialToken(editedTrimmed))
            {
                return driftWarning;
            }

            (List<int> matches, bool truncated) = CollectCandidateCompiledLineNumbers(
                editedTrimmed,
                compiledSourceLines);
            if (matches.Count == 0)
            {
                return driftWarning;
            }

            return driftWarning + FormatRequestedLineCandidateCompiledLinesSuffix(
                requestedLine,
                matches,
                truncated,
                namedCompiledMethodSpans);
        }

        /// <summary>
        /// Appends compiled-line Candidate text to a resolve-failure Message when the edited
        /// --line text appears in the last compiled source.
        /// </summary>
        internal static string AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
            string message,
            int requestedLine,
            string requestedLineEditedText,
            IReadOnlyList<string> compiledSourceLines,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans = null)
        {
            if (string.IsNullOrEmpty(message) || requestedLine <= 0)
            {
                return message ?? string.Empty;
            }

            if (string.IsNullOrEmpty(requestedLineEditedText) || compiledSourceLines == null)
            {
                return message;
            }

            string editedTrimmed = requestedLineEditedText.Trim();
            if (editedTrimmed.Length == 0)
            {
                return message;
            }

            // Why: a brace-only line matches almost every method, so its Candidate list points nowhere.
            if (PausePointCompiledLineComparisonWarnings.IsTrivialToken(editedTrimmed))
            {
                return message;
            }

            (List<int> matches, bool truncated) = CollectCandidateCompiledLineNumbers(
                editedTrimmed,
                compiledSourceLines);
            if (matches.Count == 0)
            {
                return message;
            }

            // Why no drift-warning gate: resolve-failure Messages have no drift warning, and
            // Candidate is the only compiled-line number the caller can retry with.
            return message + FormatRequestedLineCandidateCompiledLinesSuffix(
                requestedLine,
                matches,
                truncated,
                namedCompiledMethodSpans);
        }

        private static (List<int> matches, bool truncated) CollectCandidateCompiledLineNumbers(
            string editedTrimmed,
            IReadOnlyList<string> compiledSourceLines)
        {
            int matchLimit = SourcePausePointConstants.CompiledLineDriftCandidateMatchLimit;
            List<int> matches = new List<int>();
            bool truncated = false;
            for (int index = 0; index < compiledSourceLines.Count; index++)
            {
                string compiledLine = compiledSourceLines[index];
                if (compiledLine == null)
                {
                    continue;
                }

                if (!string.Equals(compiledLine.Trim(), editedTrimmed, StringComparison.Ordinal))
                {
                    continue;
                }

                if (matches.Count == matchLimit)
                {
                    truncated = true;
                    break;
                }

                matches.Add(index + 1);
            }

            return (matches, truncated);
        }

        private static string FormatCandidateCompiledLinesSuffix(
            List<int> matches,
            bool truncated,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans)
        {
            if (matches.Count == 1 && !truncated)
            {
                return string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineDriftCandidateSingleFormat,
                    FormatCandidateCompiledLine(matches[0], namedCompiledMethodSpans));
            }

            string listed = FormatCandidateCompiledLineList(matches, namedCompiledMethodSpans);
            if (truncated)
            {
                listed += string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineDriftCandidateTruncatedMatchesSuffixFormat,
                    SourcePausePointConstants.CompiledLineDriftCandidateMatchLimit);
            }

            return string.Format(
                SourcePausePointConstants.HotReloadCompiledLineDriftCandidateMultipleFormat,
                listed);
        }

        private static string FormatRequestedLineCandidateCompiledLinesSuffix(
            int requestedLine,
            List<int> matches,
            bool truncated,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans)
        {
            if (matches.Count == 1 && !truncated)
            {
                string single = string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineDriftRequestedLineCandidateSingleFormat,
                    requestedLine,
                    FormatCandidateCompiledLine(matches[0], namedCompiledMethodSpans));
                // Why no retry at the requested line itself: that is the no-drift case, so retrying
                // with --method resolves the line the same way and the advice would change nothing.
                if (matches[0] == requestedLine)
                {
                    return single;
                }

                string methodName = FindContainingCompiledMethodDisplayNameOrEmpty(
                    matches[0],
                    namedCompiledMethodSpans);
                if (methodName.Length > 0)
                {
                    return single + string.Format(
                        SourcePausePointConstants.HotReloadCompiledLineDriftCandidateRetryWithMethodSuffixFormat,
                        methodName,
                        requestedLine,
                        matches[0]);
                }

                return single + FormatGenericRetrySuffix(requestedLine);
            }

            string listed = FormatCandidateCompiledLineList(matches, namedCompiledMethodSpans);
            if (truncated)
            {
                listed += string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineDriftCandidateTruncatedMatchesSuffixFormat,
                    SourcePausePointConstants.CompiledLineDriftCandidateMatchLimit);
            }

            return string.Format(
                SourcePausePointConstants.HotReloadCompiledLineDriftRequestedLineCandidateMultipleFormat,
                requestedLine,
                listed) + FormatGenericRetrySuffix(requestedLine);
        }

        private static string FormatGenericRetrySuffix(int requestedLine)
        {
            return string.Format(
                SourcePausePointConstants.HotReloadCompiledLineDriftCandidateRetryGenericSuffixFormat,
                requestedLine);
        }

        private static string FormatCandidateCompiledLineList(
            List<int> matches,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans)
        {
            List<string> formatted = new List<string>();
            for (int index = 0; index < matches.Count; index++)
            {
                formatted.Add(FormatCandidateCompiledLine(matches[index], namedCompiledMethodSpans));
            }

            return string.Join(", ", formatted);
        }

        private static string FormatCandidateCompiledLine(
            int line,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans)
        {
            string methodName = FindContainingCompiledMethodDisplayNameOrEmpty(line, namedCompiledMethodSpans);
            if (methodName.Length > 0)
            {
                return line + string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineDriftCandidateMethodAnnotationFormat,
                    methodName);
            }

            return line.ToString();
        }

        private static string FindContainingCompiledMethodDisplayNameOrEmpty(
            int line,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans)
        {
            if (namedCompiledMethodSpans == null)
            {
                return string.Empty;
            }

            SourcePausePointNearbyCompiledMethod smallestContainingSpan = null;
            for (int index = 0; index < namedCompiledMethodSpans.Count; index++)
            {
                SourcePausePointNearbyCompiledMethod span = namedCompiledMethodSpans[index];
                if (line < span.StartLine || line > span.EndLine)
                {
                    continue;
                }

                if (smallestContainingSpan != null
                    && span.EndLine - span.StartLine >= smallestContainingSpan.EndLine - smallestContainingSpan.StartLine)
                {
                    continue;
                }

                // Why prefer the narrowest span: an enclosing method can overlap a generated
                // local-function span, while the narrow span identifies the candidate's method.
                smallestContainingSpan = span;
            }

            return smallestContainingSpan == null ? string.Empty : smallestContainingSpan.DisplayName;
        }
    }
}
