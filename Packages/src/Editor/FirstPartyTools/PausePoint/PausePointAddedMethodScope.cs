using System.Collections.Generic;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides whether a line inside a method hot reload added may still be armed against the
    /// last compiled line map: only through a --method compiled span that holds the line.
    /// </summary>
    internal static class PausePointAddedMethodScope
    {
        /// <summary>
        /// Returns the compiled span that holds the line, or null when none does.
        /// </summary>
        internal static SourcePausePointCompiledMethodSpan FindSpanContainingLineOrNull(
            IReadOnlyList<SourcePausePointCompiledMethodSpan> spans,
            int line)
        {
            if (spans == null)
            {
                return null;
            }

            for (int index = 0; index < spans.Count; index++)
            {
                SourcePausePointCompiledMethodSpan span = spans[index];
                if (span != null && IsLineInsideSpan(span, line))
                {
                    return span;
                }
            }

            return null;
        }

        /// <summary>
        /// Reports whether a resolve rounded forward out of the span that scoped an added-method
        /// line; a null span means no added method scoped the line, so nothing is out of scope.
        /// </summary>
        internal static bool IsResolvedLineOutsideScopeSpan(SourcePausePointCompiledMethodSpan span, int resolvedLine)
        {
            return span != null && !IsLineInsideSpan(span, resolvedLine);
        }

        internal static bool IsLineInsideSpan(SourcePausePointCompiledMethodSpan span, int line)
        {
            return span.StartLine <= line && line <= span.EndLine;
        }
    }
}
