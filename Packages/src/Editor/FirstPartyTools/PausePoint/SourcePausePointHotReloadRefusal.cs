using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the refusal for a pause point that resolved into a hot-reload patched method, naming
    /// the patched method and its compiled span, and pointing a line that maps into it from outside
    /// its edited body at --method.
    /// </summary>
    internal static class SourcePausePointHotReloadRefusal
    {
        internal static SourcePausePointPatchResult Build(
            MethodBase method,
            SourcePausePointResolution resolution,
            int requestedLine)
        {
            string typeName = method.DeclaringType != null ? method.DeclaringType.Name : "?";
            // The shim resolver already said the line is outside every patched body, so a compiled
            // resolve that lands in a patched method may come from an unpatched method above or
            // below it whose compiled lines the edited file no longer follows, and --method is
            // what makes the remap find it. Guidance limited to lines above the method sent a line
            // below it to --revert-all, which threw the patches away. Only an unknown compiled
            // span keeps the older wording, because the message then cannot name where the line
            // mapped to.
            bool compiledSpanKnown = resolution.CompiledMethodStartLine > 0;
            string errorMessage = compiledSpanKnown
                ? string.Format(
                    SourcePausePointConstants.HotReloadPatchedLineMapsIntoPatchedBodyMessageFormat,
                    typeName,
                    method.Name,
                    requestedLine,
                    resolution.ResolvedLine)
                : string.Format(
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
                compiledSpanKnown
                    ? string.Format(
                        SourcePausePointConstants.HotReloadPatchedLineMapsIntoPatchedBodyNextAction,
                        requestedLine)
                    : SourcePausePointConstants.HotReloadPatchedLineOutsidePatchedBodyNextAction);
        }
    }
}
