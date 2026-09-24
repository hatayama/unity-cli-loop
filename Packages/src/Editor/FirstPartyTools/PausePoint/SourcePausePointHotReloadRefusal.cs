using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the refusal for a pause point that resolved into a hot-reload patched method, naming
    /// the patched method and its compiled span.
    /// </summary>
    internal static class SourcePausePointHotReloadRefusal
    {
        internal static SourcePausePointPatchResult Build(
            MethodBase method,
            SourcePausePointResolution resolution,
            int requestedLine)
        {
            string typeName = method.DeclaringType != null ? method.DeclaringType.Name : "?";
            string errorMessage = string.Format(
                SourcePausePointConstants.HotReloadPatchedLineOutsidePatchedBodyMessageFormat,
                typeName,
                method.Name,
                requestedLine);
            if (resolution.CompiledMethodStartLine > 0 && resolution.CompiledMethodEndLine > 0)
            {
                errorMessage += string.Format(
                    SourcePausePointConstants.HotReloadPatchedCompiledMethodSpanFormat,
                    typeName,
                    method.Name,
                    resolution.CompiledMethodStartLine,
                    resolution.CompiledMethodEndLine);
            }

            return SourcePausePointPatchResult.Failure(
                SourcePausePointPatchFailureReason.MethodPatchedByHotReload,
                errorMessage,
                SourcePausePointConstants.HotReloadPatchedLineOutsidePatchedBodyNextAction);
        }
    }
}
