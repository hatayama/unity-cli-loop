using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the refusal for a pause point that resolved into a hot-reload patched method, naming
    /// the patched method and, when known, the edited body range the caller can move --line into.
    /// </summary>
    internal static class SourcePausePointHotReloadRefusal
    {
        internal static SourcePausePointPatchResult Build(
            MethodBase method,
            PausePointPatchedEditedSpan spanOrNull,
            int requestedLine)
        {
            string methodName = PausePointPatchedEditedSpanLocator.DescribeMethod(method);
            if (spanOrNull == null)
            {
                return SourcePausePointPatchResult.Failure(
                    SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                    string.Format(
                        SourcePausePointConstants.HotReloadPatchedMethodWithoutSpanRefusalMessageFormat,
                        requestedLine,
                        methodName),
                    SourcePausePointConstants.HotReloadPatchedMethodWithoutSpanRefusalNextAction);
            }

            return SourcePausePointPatchResult.Failure(
                SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalMessageFormat,
                    requestedLine,
                    methodName,
                    spanOrNull.StartLine,
                    spanOrNull.EndLine),
                string.Format(
                    SourcePausePointConstants.HotReloadPatchedMethodRefusalNextActionFormat,
                    requestedLine,
                    methodName,
                    spanOrNull.StartLine,
                    spanOrNull.EndLine));
        }
    }
}
