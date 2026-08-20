using System;
using System.Collections.Generic;
using System.IO;

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

        // Why Type.Method in the notice: enable's resolved-method string is Cecil FullName, but
        // the warning should name the Unity message the same way agents already read caller frames.
        internal static string BuildPerFrameTraceWarningOrEmpty(
            string captureMode,
            string resolvedMethod,
            int maxHistory)
        {
            if (captureMode != UloopPausePointCaptureMode.Trace || string.IsNullOrEmpty(resolvedMethod))
            {
                return string.Empty;
            }

            string simpleName = ExtractSimpleMethodName(resolvedMethod);
            if (!IsPerFrameUnityMessageSimpleName(simpleName))
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.PerFrameTraceNoticeFormat,
                FormatTypeMethodDisplay(resolvedMethod, simpleName),
                maxHistory);
        }

        private static bool IsPerFrameUnityMessageSimpleName(string simpleName)
        {
            return simpleName == "Update"
                || simpleName == "FixedUpdate"
                || simpleName == "LateUpdate"
                || simpleName == "OnGUI";
        }

        private static string ExtractSimpleMethodName(string resolvedMethod)
        {
            int colon = resolvedMethod.IndexOf("::", StringComparison.Ordinal);
            if (colon >= 0)
            {
                int start = colon + 2;
                int paren = resolvedMethod.IndexOf('(', start);
                if (paren >= 0)
                {
                    return resolvedMethod.Substring(start, paren - start);
                }

                return resolvedMethod.Substring(start);
            }

            int lastDot = resolvedMethod.LastIndexOf('.');
            string tail = lastDot >= 0 ? resolvedMethod.Substring(lastDot + 1) : resolvedMethod;
            int tailParen = tail.IndexOf('(');
            if (tailParen >= 0)
            {
                return tail.Substring(0, tailParen);
            }

            return tail;
        }

        private static string FormatTypeMethodDisplay(string resolvedMethod, string simpleName)
        {
            int colon = resolvedMethod.IndexOf("::", StringComparison.Ordinal);
            if (colon < 0)
            {
                return resolvedMethod;
            }

            string beforeColon = resolvedMethod.Substring(0, colon);
            int space = beforeColon.LastIndexOf(' ');
            string typeFullName = space >= 0 ? beforeColon.Substring(space + 1) : beforeColon;
            int typeSep = Math.Max(typeFullName.LastIndexOf('.'), typeFullName.LastIndexOf('/'));
            string typeName = typeSep >= 0 ? typeFullName.Substring(typeSep + 1) : typeFullName;
            return typeName + "." + simpleName;
        }

        // Why success-only: resolve failure leaves ResolvedMethod and ResolvedLineText empty,
        // so this wording would point at fields that are not on the response.
        // Why same resolvedLine on both sides: the resolver rounds empty/comment lines forward,
        // so comparing the requested line to the resolved line is a false drift.
        internal static string BuildCompiledLineDriftWarningOrEmpty(
            string compiledLineText,
            string editedLineText,
            string file,
            int resolvedLine)
        {
            if (string.IsNullOrEmpty(compiledLineText) || string.IsNullOrEmpty(editedLineText))
            {
                return string.Empty;
            }

            string compiledTrimmed = compiledLineText.Trim();
            string editedTrimmed = editedLineText.Trim();
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
        // like a second resolution, and empty edited text never produces drift in the first place.
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

        internal static string ReadEditedLineTextOrEmpty(string requestedFile, int resolvedLine)
        {
            if (string.IsNullOrEmpty(requestedFile) || resolvedLine <= 0)
            {
                return string.Empty;
            }

            string normalizedFile = SourcePausePointPathNormalizer.ToForwardSlashes(requestedFile);
            string absoluteFilePath = Path.Combine(UnityCliLoopPathResolver.GetProjectRoot(), normalizedFile);
            if (!File.Exists(absoluteFilePath))
            {
                return string.Empty;
            }

            return SourcePausePointSourceLineReader.ReadLineTextFromSource(
                File.ReadAllText(absoluteFilePath),
                resolvedLine);
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

        internal static string BuildCompiledLineMapWarningOrEmpty(bool hasActiveHotReloadPatches, string file)
        {
            if (!hasActiveHotReloadPatches)
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.HotReloadCompiledLineMapWarningFormat,
                SourcePausePointPathNormalizer.ToForwardSlashes(file));
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
