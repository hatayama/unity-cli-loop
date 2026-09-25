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

            IHotReloadPausePointPort hotReloadSide = HotReloadPausePointCoordination.HotReloadSide;
            if (hotReloadSide == null)
            {
                return string.Empty;
            }

            string typeName = declaringType.FullName;
            if (string.IsNullOrEmpty(typeName))
            {
                return string.Empty;
            }

            IReadOnlyList<string> addedFields = hotReloadSide.GetAddedFieldsForType(typeName);
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
        // Why fail-closed when the end is 0: the "method's closing brace" wording would be a lie.
        // methodEndLine is in the same coordinates as resolvedLine.
        internal static string BuildClosingBraceWarningOrEmpty(
            string resolvedLineText,
            int resolvedLine,
            string resolvedMethod,
            int methodEndLine)
        {
            if (string.IsNullOrEmpty(resolvedLineText) || resolvedLineText.Trim() != "}")
            {
                return string.Empty;
            }

            if (methodEndLine <= 0 || resolvedLine != methodEndLine)
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
            IReadOnlyList<string> compiledSourceLinesOrNull,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> namedCompiledMethodSpans = null)
        {
            string message = AppendNearbyCompiledMethodsSuffix(errorMessage, nearbyCompiledMethods);
            // Why skip Candidate when the edited line was not read: the Candidate sentence names
            // the text at --line N in the edited file, which is false if that read failed.
            if (!hasActiveHotReloadPatches || !requestedLineReadOk)
            {
                return message;
            }

            return PausePointCandidateCompiledLineWarnings.AppendResolveFailureRequestedLineCandidateSuffixOrUnchanged(
                message,
                requestedLine,
                requestedLineEditedText,
                compiledSourceLinesOrNull,
                namedCompiledMethodSpans);
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
