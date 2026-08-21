using System;

using io.github.hatayama.UnityCliLoop.Runtime;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Builds enable-time notices when a pause point resolves into a per-frame Unity message name.
    /// </summary>
    internal static class PausePointPerFrameEnableWarnings
    {
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

        // Why skip Trace: BuildPerFrameTraceWarningOrEmpty already owns that mode; emitting
        // both would duplicate the per-frame caution on one enable.
        internal static string BuildPerFrameImmediateHitWarningOrEmpty(
            string captureMode,
            string resolvedMethod)
        {
            if (captureMode == UloopPausePointCaptureMode.Trace || string.IsNullOrEmpty(resolvedMethod))
            {
                return string.Empty;
            }

            string simpleName = ExtractSimpleMethodName(resolvedMethod);
            if (!IsPerFrameUnityMessageSimpleName(simpleName))
            {
                return string.Empty;
            }

            return string.Format(
                SourcePausePointConstants.PerFrameImmediateHitNoticeFormat,
                FormatTypeMethodDisplay(resolvedMethod, simpleName));
        }

        internal static string MergePerFrameEnableWarnings(
            string warning,
            string captureMode,
            string resolvedMethod,
            int maxHistory)
        {
            string withTrace = PausePointEnableWarnings.MergeWarnings(
                warning,
                BuildPerFrameTraceWarningOrEmpty(captureMode, resolvedMethod, maxHistory));
            return PausePointEnableWarnings.MergeWarnings(
                withTrace,
                BuildPerFrameImmediateHitWarningOrEmpty(captureMode, resolvedMethod));
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
    }
}
