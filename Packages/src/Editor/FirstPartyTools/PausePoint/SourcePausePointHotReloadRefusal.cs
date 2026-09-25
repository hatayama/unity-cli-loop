using System.Reflection;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the refusal for a pause point that resolved into a hot-reload patched method: the
    /// edited body range when the file on disk still follows the running patch, and otherwise the
    /// reason no line of the file can reach that patch and what changes it.
    /// </summary>
    internal static class SourcePausePointHotReloadRefusal
    {
        // Why the range comes first: when it is known, the patch is the latest generation's and
        // its lines follow the file on disk, so moving --line into it arms the running code.
        // Why "edited since" is checked before the lookup: a reload of the file as it is now
        // records the range again, which is the one next action that changes the answer.
        internal static SourcePausePointPatchResult Build(
            MethodBase method,
            string normalizedFile,
            int requestedLine)
        {
            string methodName = PausePointPatchedEditedSpanLocator.DescribeMethod(method);
            PausePointPatchedEditedSpan span =
                PausePointPatchedEditedSpanLocator.FindPatchedSpanOfMethodOrNull(normalizedFile, method);
            if (span != null)
            {
                return WithEditedRange(methodName, span, requestedLine);
            }

            PausePointHotReloadFileState fileState = PausePointHotReloadFileState.Read(normalizedFile);
            if (fileState.EditedSinceLatestReload)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.PatchedSourceChangedSinceReload,
                    string.Format(
                        SourcePausePointConstants.PatchedSourceChangedOnDiskMessageFormat,
                        normalizedFile,
                        requestedLine),
                    SourcePausePointConstants.PatchedSourceChangedOnDiskHint);
            }

            // Why a method in the latest generation keeps the range-less text: its patch is the
            // latest reload's, so saying an earlier reload's body runs would be wrong; only the
            // range was not recorded.
            if (fileState.ShimSpansFollowFile
                && PausePointPatchedEditedSpanLocator.IsMethodInShimLookup(normalizedFile, method))
            {
                return WithoutEditedRange(methodName, requestedLine);
            }

            if (fileState.LatestReloadReadFileAsItIs)
            {
                return LeftBehind(fileState.FindUnappliedRowForMethodOrNull(method), methodName, requestedLine);
            }

            // Why the range-less text without a record: nothing says which reload left this patch,
            // so it cannot be called an earlier one.
            return WithoutEditedRange(methodName, requestedLine);
        }

        private static SourcePausePointPatchResult WithEditedRange(
            string methodName,
            PausePointPatchedEditedSpan span,
            int requestedLine)
        {
            return SourcePausePointPatchResult.Failure(
                SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalMessageFormat,
                    requestedLine,
                    methodName,
                    span.StartLine,
                    span.EndLine),
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat,
                    requestedLine,
                    methodName,
                    span.StartLine,
                    span.EndLine));
        }

        private static SourcePausePointPatchResult WithoutEditedRange(string methodName, int requestedLine)
        {
            return SourcePausePointPatchResult.Failure(
                SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodWithoutSpanRefusalMessageFormat,
                    requestedLine,
                    methodName),
                SourcePausePointConstants.HotReloadPatchedMethodWithoutSpanRefusalNextAction);
        }

        // Why a row changes the next action: the row's Reason names what to change so that a
        // reload applies the method again, whereas without a row only a compile replaces the
        // earlier body.
        private static SourcePausePointPatchResult LeftBehind(
            HotReloadUnappliedRow rowOrNull,
            string methodName,
            int requestedLine)
        {
            if (rowOrNull == null)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                    string.Format(
                        SourcePausePointConstants.HotReloadEarlierPatchRefusalMessageFormat,
                        requestedLine,
                        methodName),
                    SourcePausePointConstants.HotReloadEarlierPatchRefusalNextAction);
            }

            string verb = rowOrNull.Kind == HotReloadUnappliedRowKind.Skipped
                ? SourcePausePointConstants.HotReloadLeftBehindSkippedVerb
                : SourcePausePointConstants.HotReloadLeftBehindFailedVerb;
            return SourcePausePointPatchResult.Failure(
                SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                string.Format(
                    SourcePausePointConstants.HotReloadLeftBehindMethodRefusalMessageFormat,
                    requestedLine,
                    methodName,
                    verb,
                    rowOrNull.Label),
                SourcePausePointConstants.HotReloadLeftBehindMethodRefusalNextAction);
        }
    }
}
