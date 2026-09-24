using System;
using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Resolves --method/--line by matching the edited line's text inside that method's compiled
    /// span first, and falls back to the plain resolve.
    /// </summary>
    internal static class PausePointEditedLineRemap
    {
        private const int DeclarationLineLookbackLimit = 6;

        // Why text first: after a hot reload the edited line numbers drift from the compiled
        // spans, and the plain resolve rounds to the first sequence point at or after --line,
        // so it succeeds on a different statement. A unique text match is the intended one.
        internal static (SourcePausePointResolveResult resolveResult, string remapWarning)
            ResolveWithEditedLineRemap(
                string file,
                int line,
                string method,
                SourcePausePointSnapshotTiming snapshotTiming)
        {
            if (!string.IsNullOrEmpty(method) && line > 0 && !string.IsNullOrEmpty(file))
            {
                int remappedLine = FindTextMatchedCompiledLineOrZero(file, line, method);
                if (remappedLine > 0 && remappedLine != line)
                {
                    SourcePausePointResolveResult retry =
                        SourcePausePointResolver.Resolve(file, remappedLine, method, snapshotTiming);
                    // Why exact line: Resolve rounds a comment or continuation forward, and the
                    // remap warning claims the marker was placed at remappedLine.
                    if (retry.Success && retry.Resolution.ResolvedLine == remappedLine)
                    {
                        return (
                            retry,
                            PausePointEnableWarnings.BuildEditedLineRemapWarning(line, method, remappedLine));
                    }
                }
            }

            return (SourcePausePointResolver.Resolve(file, line, method, snapshotTiming), string.Empty);
        }

        private static int FindTextMatchedCompiledLineOrZero(string file, int line, string method)
        {
            // Why snapshot-only: the on-disk file can already include uncompiled edits, so
            // scanning it against the last PDB span can unique-match a later statement onto
            // an old sequence-point line and still pass the exact-line pin.
            string compiledSnapshotSource = PausePointCompiledSourceReader.LoadSnapshotOrEmpty(file);
            if (string.IsNullOrEmpty(compiledSnapshotSource))
            {
                return 0;
            }

            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans =
                SourcePausePointResolver.FindCompiledMethodSpans(file, method);
            if (spans.Count == 0)
            {
                return 0;
            }

            (bool readOk, string editedLineText) =
                PausePointCompiledLineComparisonWarnings.ReadEditedLineText(file, line);
            if (!readOk)
            {
                return 0;
            }

            string editedTrimmed = editedLineText.Trim();
            // Why skip braces: "{" is in every method, and resolving it to the entry is the
            // plain resolve's job, not a text match's.
            if (editedTrimmed.Length == 0 || PausePointCompiledLineComparisonWarnings.IsTrivialToken(editedTrimmed))
            {
                return 0;
            }

            string[] compiledSourceLines =
                SourcePausePointSourceLineReader.SplitSourceLines(compiledSnapshotSource);
            if (IsUndriftedLineInsideMethodRegion(line, editedTrimmed, compiledSourceLines, spans))
            {
                return 0;
            }

            return FindUniqueMatchingCompiledLineOrZero(method, editedLineText, compiledSourceLines, spans);
        }

        // Why compare text rather than the returned line: a declaration match is reported as the
        // span start, which never equals the declaration line, so the region the text match
        // covers (span lines plus declaration lines) is the region where same text means no drift.
        private static bool IsUndriftedLineInsideMethodRegion(
            int line,
            string editedTrimmed,
            IReadOnlyList<string> compiledSourceLines,
            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans)
        {
            if (line > compiledSourceLines.Count)
            {
                return false;
            }

            string compiledText = compiledSourceLines[line - 1];
            if (compiledText == null
                || !string.Equals(compiledText.Trim(), editedTrimmed, StringComparison.Ordinal))
            {
                return false;
            }

            for (int spanIndex = 0; spanIndex < spans.Count; spanIndex++)
            {
                SourcePausePointCompiledMethodSpan span = spans[spanIndex];
                if (span == null)
                {
                    continue;
                }

                int regionTopLine = FindDeclarationRegionTopLine(compiledSourceLines, span);
                if (line >= regionTopLine && line <= span.EndLine)
                {
                    return true;
                }
            }

            return false;
        }

        // Why every span line: file-wide candidate search stops at three hits and cannot prove
        // uniqueness; a match outside the named method's compiled span must not count.
        internal static int FindUniqueMatchingCompiledLineOrZero(
            string methodFilter,
            string editedLineText,
            IReadOnlyList<string> compiledSourceLines,
            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans)
        {
            if (string.IsNullOrEmpty(methodFilter)
                || string.IsNullOrEmpty(editedLineText)
                || compiledSourceLines == null
                || spans == null)
            {
                return 0;
            }

            string editedTrimmed = editedLineText.Trim();
            if (editedTrimmed.Length == 0)
            {
                return 0;
            }

            int matchingLine = 0;
            int matchCount = 0;
            for (int spanIndex = 0; spanIndex < spans.Count; spanIndex++)
            {
                SourcePausePointCompiledMethodSpan span = spans[spanIndex];
                if (span == null)
                {
                    continue;
                }

                for (int compiledLine = span.StartLine; compiledLine <= span.EndLine; compiledLine++)
                {
                    if (compiledLine > compiledSourceLines.Count)
                    {
                        continue;
                    }

                    string compiledText = compiledSourceLines[compiledLine - 1];
                    if (compiledText == null)
                    {
                        continue;
                    }

                    if (!string.Equals(compiledText.Trim(), editedTrimmed, StringComparison.Ordinal))
                    {
                        continue;
                    }

                    matchCount++;
                    matchingLine = compiledLine;
                }

                // The compiled span starts at the first sequence point, so the declaration line an
                // agent copies from the edited file never lies inside it.
                matchCount += CountDeclarationLineMatchesAboveSpan(
                    editedTrimmed, compiledSourceLines, span, ref matchingLine);
            }

            if (matchCount != 1)
            {
                return 0;
            }

            return matchingLine;
        }

        private static int CountDeclarationLineMatchesAboveSpan(
            string editedTrimmed,
            IReadOnlyList<string> compiledSourceLines,
            SourcePausePointCompiledMethodSpan span,
            ref int matchingLine)
        {
            int matches = 0;
            int regionTopLine = FindDeclarationRegionTopLine(compiledSourceLines, span);
            for (int line = span.StartLine - 1; line >= regionTopLine; line--)
            {
                if (line > compiledSourceLines.Count)
                {
                    continue;
                }

                string text = compiledSourceLines[line - 1];
                string trimmed = text == null ? string.Empty : text.Trim();
                if (!string.Equals(trimmed, editedTrimmed, StringComparison.Ordinal))
                {
                    continue;
                }

                matches++;
                // The span start rather than the declaration line: the retry requires
                // ResolvedLine == remappedLine, and a declaration line has no sequence point.
                matchingLine = span.StartLine;
            }

            return matches;
        }

        /// <summary>
        /// Returns the topmost declaration line above the span, or the span start when none.
        /// </summary>
        private static int FindDeclarationRegionTopLine(
            IReadOnlyList<string> compiledSourceLines,
            SourcePausePointCompiledMethodSpan span)
        {
            int topLine = span.StartLine;
            for (int line = span.StartLine - 1;
                 line >= 1 && line >= span.StartLine - DeclarationLineLookbackLimit;
                 line--)
            {
                if (line > compiledSourceLines.Count)
                {
                    topLine = line;
                    continue;
                }

                string text = compiledSourceLines[line - 1];
                string trimmed = text == null ? string.Empty : text.Trim();
                // A blank line or a line ending a block or a statement is not part of this
                // method's declaration, and going past it would match the previous member.
                if (trimmed.Length == 0 || EndsDeclarationRegion(trimmed))
                {
                    break;
                }

                topLine = line;
            }

            return topLine;
        }

        private static bool EndsDeclarationRegion(string trimmed)
        {
            return trimmed.EndsWith("}", StringComparison.Ordinal)
                || trimmed.EndsWith("{", StringComparison.Ordinal)
                || trimmed.EndsWith(";", StringComparison.Ordinal);
        }
    }
}
