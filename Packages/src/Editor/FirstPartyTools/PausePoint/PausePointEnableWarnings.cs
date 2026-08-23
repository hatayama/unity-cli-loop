using System;
using System.Collections.Generic;

using UnityEditor;
using UnityEngine;

using io.github.hatayama.UnityCliLoop.Runtime;
using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds enable-pause-point warning and next-action strings without changing registry state.
    /// </summary>
    internal static class PausePointEnableWarnings
    {
        // Why after patch warnings: existing enable pins compose CreateEnableWarning + patch
        // warnings; appending here keeps those strings unchanged when the type has no added fields.
        internal static string BuildAddedFieldsNotCapturedWarningOrEmpty(Type declaringType)
        {
            if (declaringType == null)
            {
                return string.Empty;
            }

            Func<string, IReadOnlyList<string>> getter =
                HotReloadPausePointCoordination.GetAddedFieldsForType;
            if (getter == null)
            {
                return string.Empty;
            }

            string typeName = declaringType.FullName;
            if (string.IsNullOrEmpty(typeName))
            {
                return string.Empty;
            }

            IReadOnlyList<string> addedFields = getter(typeName);
            if (addedFields == null || addedFields.Count == 0)
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.HotReloadAddedFieldsNotCapturedWarningFormat,
                typeName,
                addedFields.Count,
                string.Join(", ", addedFields));
        }

        internal static string MergeWarnings(string first, string second)
        {
            if (string.IsNullOrEmpty(first))
            {
                return second;
            }

            if (string.IsNullOrEmpty(second))
            {
                return first;
            }

            return first + " " + second;
        }

        // Why only when empty: drift and other success-path next-actions must keep their wording.
        internal static string ResolveSuccessEnableRecommendedNextAction(string existing, string id)
        {
            Debug.Assert(!string.IsNullOrEmpty(id), "id must not be empty");
            if (!string.IsNullOrEmpty(existing))
            {
                return existing;
            }

            return string.Format(
                SourcePausePointConstants.EnableSuccessArmingRecommendedNextActionFormat,
                id);
        }

        // Why HitCount or Hit: Continuous/Trace stay Enabled after a hit, so Status==Hit alone
        // would miss a re-arm that still discards capture history.
        internal static string BuildRearmDiscardWarningOrEmpty(UloopPausePointSnapshot previous)
        {
            Debug.Assert(previous != null, "previous must not be null");
            if (previous.HitCount <= 0 && previous.Status != UloopPausePointStatus.Hit)
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.RearmDiscardCapturedVariablesWarningFormat,
                previous.Generation);
        }

        // Why also match method end: a using/lock Dispose nested "}" has a sequence point, so
        // Trim()=="}" alone would claim every return path reaches an inner brace.
        // Why fail-closed when both ends are 0: the "method's closing brace" wording would be a lie.
        internal static string BuildClosingBraceWarningOrEmpty(
            string resolvedLineText,
            int resolvedLine,
            string resolvedMethod,
            int compiledMethodEndLine,
            int editedMethodEndLine)
        {
            if (string.IsNullOrEmpty(resolvedLineText) || resolvedLineText.Trim() != "}")
            {
                return string.Empty;
            }

            bool atCompiledMethodEnd = compiledMethodEndLine > 0 && resolvedLine == compiledMethodEndLine;
            bool atEditedMethodEnd = editedMethodEndLine > 0 && resolvedLine == editedMethodEndLine;
            if (!atCompiledMethodEnd && !atEditedMethodEnd)
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.ClosingBraceResolvedLineWarningFormat,
                resolvedLine,
                resolvedMethod);
        }

        internal static string AppendCompiledMethodSpanToDriftWarningOrUnchanged(
            string driftWarning,
            string resolvedMethod,
            int compiledMethodStartLine,
            int compiledMethodEndLine)
        {
            if (string.IsNullOrEmpty(driftWarning)
                || compiledMethodStartLine <= 0
                || compiledMethodEndLine <= 0)
            {
                return driftWarning ?? string.Empty;
            }

            return driftWarning + string.Format(
                SourcePausePointConstants.HotReloadCompiledMethodSpanInLastCompiledSourceFormat,
                resolvedMethod,
                compiledMethodStartLine,
                compiledMethodEndLine);
        }

        // Why only after a non-empty drift warning: a candidate list without drift would look
        // like a second resolution.
        // Why skip empty edited text: a blank line has no statement to locate in compiled source.
        internal static string AppendCandidateCompiledLinesToDriftWarningOrUnchanged(
            string driftWarning,
            string editedLineText,
            IReadOnlyList<string> compiledSourceLines)
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

            return driftWarning + FormatCandidateCompiledLinesSuffix(matches, truncated);
        }

        // Why a distinct sentence: the resolved-line candidate does not name --line, so two
        // identical "edited line" suffixes would not say which search produced which hit.
        internal static string AppendRequestedLineCandidateCompiledLinesToDriftWarningOrUnchanged(
            string driftWarning,
            int requestedLine,
            string requestedLineEditedText,
            IReadOnlyList<string> compiledSourceLines)
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
                truncated);
        }

        /// <summary>
        /// Appends compiled-line Candidate text to a resolve-failure Message when the edited
        /// --line text appears in the last compiled source.
        /// </summary>
        internal static string AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
            string message,
            int requestedLine,
            string requestedLineEditedText,
            IReadOnlyList<string> compiledSourceLines)
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
                truncated);
        }

        /// <summary>
        /// Builds a resolve-failure Message: Nearby methods, then Candidate when hot-reload
        /// patches are active and the edited --line text was read.
        /// </summary>
        internal static string BuildResolveFailureMessage(
            string errorMessage,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> nearbyCompiledMethods,
            bool hasActiveHotReloadPatches,
            int requestedLine,
            bool requestedLineReadOk,
            string requestedLineEditedText,
            IReadOnlyList<string> compiledSourceLinesOrNull)
        {
            string message = AppendNearbyCompiledMethodsSuffix(errorMessage, nearbyCompiledMethods);
            // Why skip Candidate when the edited line was not read: the Candidate sentence names
            // the text at --line N in the edited file, which is false if that read failed.
            if (!hasActiveHotReloadPatches || !requestedLineReadOk)
            {
                return message;
            }

            return AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                requestedLine,
                requestedLineEditedText,
                compiledSourceLinesOrNull);
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

        private static string FormatCandidateCompiledLinesSuffix(List<int> matches, bool truncated)
        {
            if (matches.Count == 1 && !truncated)
            {
                return string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineDriftCandidateSingleFormat,
                    matches[0]);
            }

            string listed = string.Join(", ", matches);
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
            bool truncated)
        {
            if (matches.Count == 1 && !truncated)
            {
                return string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineDriftRequestedLineCandidateSingleFormat,
                    requestedLine,
                    matches[0]);
            }

            string listed = string.Join(", ", matches);
            if (truncated)
            {
                listed += string.Format(
                    SourcePausePointConstants.HotReloadCompiledLineDriftCandidateTruncatedMatchesSuffixFormat,
                    SourcePausePointConstants.CompiledLineDriftCandidateMatchLimit);
            }

            return string.Format(
                SourcePausePointConstants.HotReloadCompiledLineDriftRequestedLineCandidateMultipleFormat,
                requestedLine,
                listed);
        }

        internal static string BuildRetargetedToHotReloadPatchWarningOrEmpty(
            bool retargetedToHotReloadPatch,
            string resolvedMethod,
            int requestedLine,
            int editedMethodStartLine,
            int editedMethodEndLine)
        {
            if (!retargetedToHotReloadPatch)
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.HotReloadRetargetedToEditedFileWarningFormat,
                resolvedMethod,
                requestedLine,
                editedMethodStartLine,
                editedMethodEndLine);
        }

        internal static string AppendNearbyCompiledMethodsSuffix(
            string errorMessage,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> nearbyCompiledMethods)
        {
            if (nearbyCompiledMethods == null || nearbyCompiledMethods.Count == 0)
            {
                return errorMessage;
            }

            List<string> parts = new List<string>();
            foreach (SourcePausePointNearbyCompiledMethod nearby in nearbyCompiledMethods)
            {
                parts.Add(
                    string.Format(
                        SourcePausePointConstants.NearbyCompiledMethodSpanFormat,
                        nearby.DisplayName,
                        nearby.StartLine,
                        nearby.EndLine));
            }

            return errorMessage
                + SourcePausePointConstants.NearbyCompiledMethodsPrefix
                + string.Join("; ", parts)
                + ".";
        }

        internal static string BuildPatchedMethodPdbUnavailableWarningOrEmpty(
            bool patchedMethodPdbUnavailable,
            string methodDisplayName,
            int requestedLine)
        {
            if (!patchedMethodPdbUnavailable)
            {
                return string.Empty;
            }

            Debug.Assert(!string.IsNullOrEmpty(methodDisplayName), "methodDisplayName must not be empty.");
            Debug.Assert(requestedLine > 0, "requestedLine must be a positive 1-based line number.");
            return string.Format(
                SourcePausePointConstants.HotReloadPatchedMethodPdbUnavailableWarningFormat,
                methodDisplayName,
                requestedLine);
        }

        internal static string ChooseCompiledLineMapWarning(
            string patchedMethodPdbUnavailableWarning,
            string genericCompiledLineMapWarning)
        {
            if (!string.IsNullOrEmpty(patchedMethodPdbUnavailableWarning))
            {
                return patchedMethodPdbUnavailableWarning;
            }

            return genericCompiledLineMapWarning;
        }

        internal static string BuildCompiledLineMapWarningOrEmpty(
            bool hasActiveHotReloadPatches,
            string file,
            string resolvedMethod,
            bool comparedAndMatched)
        {
            if (!hasActiveHotReloadPatches)
            {
                return string.Empty;
            }

            Debug.Assert(!string.IsNullOrEmpty(resolvedMethod), "resolvedMethod must not be empty.");
            string format = comparedAndMatched
                ? SourcePausePointConstants.HotReloadCompiledLineMapMatchedWarningFormat
                : SourcePausePointConstants.HotReloadCompiledLineMapWarningFormat;
            return string.Format(
                format,
                SourcePausePointPathNormalizer.ToForwardSlashes(file),
                resolvedMethod);
        }

        internal static string BuildCompiledLineMapResolveFailureWarningOrEmpty(
            bool hasActiveHotReloadPatches,
            string file)
        {
            if (!hasActiveHotReloadPatches)
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapResolveFailureWarningFormat,
                SourcePausePointPathNormalizer.ToForwardSlashes(file));
        }

        internal static string BuildEditedLineRemapWarning(
            int originalLine,
            string methodName,
            int remappedLine)
        {
            Debug.Assert(originalLine > 0, "originalLine must be a positive 1-based line number.");
            Debug.Assert(!string.IsNullOrEmpty(methodName), "methodName must not be empty.");
            Debug.Assert(remappedLine > 0, "remappedLine must be a positive 1-based line number.");
            return string.Format(
                SourcePausePointConstants.EditedLineRemapWarningFormat,
                originalLine,
                methodName,
                remappedLine);
        }

        internal static string CreateEnableWarning()
        {
            if (EditorApplication.isPlaying)
            {
                return string.Empty;
            }

            if (IsDomainReloadDisabledOnEnterPlayMode())
            {
                return string.Empty;
            }

            return "Pause point was enabled before PlayMode while Domain Reload is enabled. " +
                   "Entering PlayMode may clear this marker; keep Domain Reload disabled for this workflow or enable the marker after PlayMode starts.";
        }

        private static bool IsDomainReloadDisabledOnEnterPlayMode()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled)
            {
                return false;
            }

            return (EditorSettings.enterPlayModeOptions & EnterPlayModeOptions.DisableDomainReload) != 0;
        }
    }
}
