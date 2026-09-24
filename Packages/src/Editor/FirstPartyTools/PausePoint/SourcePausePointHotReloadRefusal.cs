using System.Reflection;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds the refusal for a pause point that resolved into a hot-reload patched method, naming
    /// the patched method and its compiled span, and pointing a line above that method at --method.
    /// </summary>
    internal static class SourcePausePointHotReloadRefusal
    {
        internal static SourcePausePointPatchResult Build(
            MethodBase method,
            SourcePausePointResolution resolution,
            int requestedLine)
        {
            string typeName = method.DeclaringType != null ? method.DeclaringType.Name : "?";
            // The shim resolver already said the line is outside every patched body; a compiled
            // snap that lands in a patched method from above it may come from an unpatched method
            // whose compiled lines the edited file no longer follows, and --method is what makes
            // the remap find it. Why the guidance stays conditional: a blank or comment line just
            // above the patched method snaps the same way, and the line-to-method mapping that
            // would tell the two apart is exactly what line drift breaks.
            bool requestedLineIsAbovePatchedMethod =
                resolution.CompiledMethodStartLine > 0 && requestedLine < resolution.CompiledMethodStartLine;
            string errorMessage = requestedLineIsAbovePatchedMethod
                ? string.Format(
                    SourcePausePointConstants.HotReloadPatchedLineBeforePatchedBodyMessageFormat,
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
                requestedLineIsAbovePatchedMethod
                    ? string.Format(
                        SourcePausePointConstants.HotReloadPatchedLineBeforePatchedBodyNextAction,
                        requestedLine)
                    : SourcePausePointConstants.HotReloadPatchedLineOutsidePatchedBodyNextAction);
        }
    }
}
