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

        // nearbyPrefix names the line numbers the spans are in, which differ by line basis.
        internal static string AppendNearbyCompiledMethodsSuffix(
            string errorMessage,
            string nearbyPrefix,
            IReadOnlyList<SourcePausePointNearbyCompiledMethod> nearbyCompiledMethods)
        {
            Debug.Assert(!string.IsNullOrEmpty(nearbyPrefix), "nearbyPrefix must name the line numbers of the spans.");
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
                + nearbyPrefix
                + string.Join("; ", parts)
                + ".";
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
