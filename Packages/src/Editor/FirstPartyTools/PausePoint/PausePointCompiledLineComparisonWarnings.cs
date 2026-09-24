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
        internal static (string warning, bool comparedAndMatched) BuildCompiledLineDriftWarningOrEmpty(
            string compiledLineText,
            string editedLineText,
            string file,
            int resolvedLine,
            bool editedLineReadOk)
        {
            if (string.IsNullOrEmpty(compiledLineText) || !editedLineReadOk)
            {
                return (string.Empty, false);
            }

            string compiledTrimmed = compiledLineText.Trim();
            string editedTrimmed = editedLineText == null ? string.Empty : editedLineText.Trim();
            if (editedTrimmed.Length == 0)
            {
                return (string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineMapBlankEditedLineDriftWarningFormat,
                    SourcePausePointPathNormalizer.ToForwardSlashes(file),
                    resolvedLine,
                    compiledTrimmed), false);
            }

            if (string.Equals(compiledTrimmed, editedTrimmed, StringComparison.Ordinal))
            {
                // A brace or terminator matches almost anywhere, so it proves nothing about drift.
                return (string.Empty, !IsTrivialToken(compiledTrimmed));
            }

            return (string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapLineDriftWarningFormat,
                SourcePausePointPathNormalizer.ToForwardSlashes(file),
                resolvedLine,
                compiledTrimmed,
                editedTrimmed), false);
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

        /// <summary>
        /// Reads the edited lines behind an enable and composes its drift and snap warning.
        /// </summary>
        // Why the early return comes first: without a compiled comparison the edited-file reads
        // and the snapshot split would be wasted IO.
        internal static (string warning, bool comparedAndMatched) BuildEnableComparisonWarningOrEmpty(
            bool compareCompiledLineDrift,
            string file,
            int requestedLine,
            int resolvedLine,
            string resolvedMethod,
            string compiledResolvedLineText,
            string compiledSnapshotSource,
            int compiledMethodStartLine,
            int compiledMethodEndLine)
        {
            if (!compareCompiledLineDrift)
            {
                return (string.Empty, false);
            }

            (bool resolvedEditedReadOk, string resolvedEditedLineText) = ReadEditedLineText(file, resolvedLine);
            (bool requestedEditedReadOk, string requestedEditedLineText) = ReadEditedLineText(file, requestedLine);
            string[] compiledSourceLines = SourcePausePointSourceLineReader.SplitSourceLines(compiledSnapshotSource);
            return ComposeCompiledLineDriftAndSnapWarningOrEmpty(
                file,
                requestedLine,
                resolvedLine,
                resolvedMethod,
                compiledResolvedLineText,
                resolvedEditedReadOk,
                resolvedEditedLineText,
                requestedEditedReadOk,
                requestedEditedLineText,
                compiledMethodStartLine,
                compiledMethodEndLine,
                compiledSourceLines);
        }

        // Why snap before resolved-line drift: the requested line is what the agent passed;
        // the armed line is what actually paused.
        // Why resolved-text candidates only with a drift sentence: a snap-only warning already
        // named the armed line, so searching that same text finds the armed line itself.
        // Why still search requested-line text on a snap-only warning: the intended statement is
        // on --line, including when braces at the armed line happen to match.
        // Why skip the requested-line search when texts match: the suffix would duplicate.
        internal static (string warning, bool comparedAndMatched) ComposeCompiledLineDriftAndSnapWarningOrEmpty(
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
            (string driftWarning, bool comparedAndMatched) = BuildResolvedLineDriftWarningOrEmpty(
                file,
                requestedLine,
                resolvedLine,
                resolvedMethod,
                compiledResolvedLineText,
                resolvedEditedLineReadOk,
                resolvedEditedLineText);
            string combined = PausePointEnableWarnings.MergeWarnings(snapWarning, driftWarning);
            string resolvedTrimmed = resolvedEditedLineText == null
                ? string.Empty
                : resolvedEditedLineText.Trim();
            string requestedTrimmed = requestedEditedLineText == null
                ? string.Empty
                : requestedEditedLineText.Trim();
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans =
                Array.Empty<SourcePausePointNearbyCompiledMethod>();
            if (driftWarning.Length > 0
                || !string.Equals(resolvedTrimmed, requestedTrimmed, StringComparison.Ordinal))
            {
                namedCompiledMethodSpans = SourcePausePointResolver.FindNamedCompiledMethodSpansInFile(file);
            }
            combined = PausePointEnableWarnings.AppendCompiledMethodSpanToDriftWarningOrUnchanged(
                combined,
                resolvedMethod,
                compiledMethodStartLine,
                compiledMethodEndLine);
            if (driftWarning.Length > 0)
            {
                combined = PausePointCandidateCompiledLineWarnings.AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
                    combined,
                    resolvedEditedLineText,
                    compiledSourceLines,
                    namedCompiledMethodSpans);
            }

            if (!string.Equals(resolvedTrimmed, requestedTrimmed, StringComparison.Ordinal))
            {
                combined = PausePointCandidateCompiledLineWarnings.AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged(
                    combined,
                    requestedLine,
                    requestedEditedLineText,
                    compiledSourceLines,
                    namedCompiledMethodSpans);
            }

            return (combined, comparedAndMatched);
        }

        // Why the patched-span check comes before the text comparison: a requested line outside
        // every patched body that resolves to a line the edited file places inside a patched
        // method proves the file drifted, even when the two texts happen to match.
        // Why the requested line must be outside every patched body: a patched method with no
        // shim PDB also falls back to the compiled resolve, and then both lines sit in that
        // method's own span without any drift.
        private static (string warning, bool comparedAndMatched) BuildResolvedLineDriftWarningOrEmpty(
            string file,
            int requestedLine,
            int resolvedLine,
            string resolvedMethod,
            string compiledResolvedLineText,
            bool resolvedEditedLineReadOk,
            string resolvedEditedLineText)
        {
            string patchedMethod = PausePointPatchedEditedSpanLocator.FindPatchedMethodContainingEditedLineOrNull(
                file,
                resolvedLine);
            bool requestedLineInsidePatchedBody =
                PausePointPatchedEditedSpanLocator.FindPatchedMethodContainingEditedLineOrNull(file, requestedLine) != null;
            if (patchedMethod == null || requestedLineInsidePatchedBody)
            {
                return BuildCompiledLineDriftWarningOrEmpty(
                    compiledResolvedLineText,
                    resolvedEditedLineText,
                    file,
                    resolvedLine,
                    resolvedEditedLineReadOk);
            }

            return (string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapPatchedSpanDriftWarningFormat,
                SourcePausePointPathNormalizer.ToForwardSlashes(file),
                resolvedLine,
                resolvedMethod ?? string.Empty,
                patchedMethod), false);
        }

        private static bool IsTrivialToken(string trimmed)
        {
            foreach (char character in trimmed)
            {
                if (character != '{' && character != '}' && character != '(' && character != ')'
                    && character != ';' && !char.IsWhiteSpace(character))
                {
                    return false;
                }
            }

            return true;
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
