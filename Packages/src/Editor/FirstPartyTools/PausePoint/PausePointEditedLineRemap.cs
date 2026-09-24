using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Remaps a failed --method/--line resolve onto the unique matching line inside that
    /// method's compiled span or on its declaration lines, or leaves the original failure
    /// unchanged.
    /// </summary>
    internal static class PausePointEditedLineRemap
    {
        private const int DeclarationLineLookbackLimit = 6;

        internal static (SourcePausePointResolveResult resolveResult, string remapWarning)
            ResolveWithEditedLineRemap(
                string file,
                int line,
                string method,
                SourcePausePointSnapshotTiming snapshotTiming)
        {
            SourcePausePointResolveResult resolveResult =
                SourcePausePointResolver.Resolve(file, line, method, snapshotTiming);
            if (resolveResult.Success)
            {
                return (resolveResult, string.Empty);
            }

            return TryRemapAfterResolveFailure(resolveResult, file, line, method, snapshotTiming);
        }

        internal static (SourcePausePointResolveResult resolveResult, string remapWarning)
            TryRemapAfterResolveFailure(
                SourcePausePointResolveResult failedResult,
                string file,
                int line,
                string method,
                SourcePausePointSnapshotTiming snapshotTiming)
        {
            Debug.Assert(failedResult != null, "failedResult must not be null.");
            Debug.Assert(!failedResult.Success, "TryRemapAfterResolveFailure requires a failed resolve.");

            if (string.IsNullOrEmpty(method) || line <= 0 || string.IsNullOrEmpty(file))
            {
                return (failedResult, string.Empty);
            }

            // Why snapshot-only: the on-disk file can already include uncompiled edits, so
            // scanning it against the last PDB span can unique-match a later statement onto
            // an old sequence-point line and still pass the exact-line pin.
            string compiledSnapshotSource = PausePointCompiledSourceReader.LoadSnapshotOrEmpty(file);
            if (string.IsNullOrEmpty(compiledSnapshotSource))
            {
                return (failedResult, string.Empty);
            }

            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans =
                SourcePausePointResolver.FindCompiledMethodSpans(file, method);
            if (spans.Count == 0)
            {
                return (failedResult, string.Empty);
            }

            (bool readOk, string editedLineText) =
                PausePointCompiledLineComparisonWarnings.ReadEditedLineText(file, line);
            if (!readOk)
            {
                return (failedResult, string.Empty);
            }

            string[] compiledSourceLines =
                SourcePausePointSourceLineReader.SplitSourceLines(compiledSnapshotSource);
            int remappedLine = FindUniqueMatchingCompiledLineOrZero(
                method,
                editedLineText,
                compiledSourceLines,
                spans);
            if (remappedLine <= 0)
            {
                return (failedResult, string.Empty);
            }

            SourcePausePointResolveResult retry =
                SourcePausePointResolver.Resolve(file, remappedLine, method, snapshotTiming);
            // Why exact line: Resolve rounds a comment or continuation forward, and the
            // remap warning claims the marker was placed at remappedLine.
            if (!retry.Success || retry.Resolution.ResolvedLine != remappedLine)
            {
                return (failedResult, string.Empty);
            }

            return (
                retry,
                PausePointEnableWarnings.BuildEditedLineRemapWarning(line, method, remappedLine));
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
            for (int line = span.StartLine - 1;
                 line >= 1 && line >= span.StartLine - DeclarationLineLookbackLimit;
                 line--)
            {
                if (line > compiledSourceLines.Count)
                {
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

        private static bool EndsDeclarationRegion(string trimmed)
        {
            return trimmed.EndsWith("}", StringComparison.Ordinal)
                || trimmed.EndsWith("{", StringComparison.Ordinal)
                || trimmed.EndsWith(";", StringComparison.Ordinal);
        }
    }
}
