using System.Collections.Generic;

using io.github.hatayama.UnityCliLoop.ToolContracts;

namespace io.github.hatayama.UnityCliLoop.FirstPartyTools
{
    /// <summary>
    /// Decides whether a line inside a method hot reload added may still be armed against the
    /// last compiled line map: only through a --method compiled span that holds the line, and
    /// only when that --method does not also name the added method.
    /// </summary>
    internal static class PausePointAddedMethodScope
    {
        /// <summary>
        /// Returns the method hot reload added to the file whose source range holds the line, or
        /// null when there is none or no hot reload side is installed.
        /// </summary>
        internal static HotReloadAddedMethodAtLine FindAddedMethodContainingLineOrNull(string normalizedFile, int line)
        {
            return HotReloadPausePointCoordination.HotReloadSide?.FindAddedMethodContainingLine(
                normalizedFile,
                line);
        }

        /// <summary>
        /// Returns the compiled span a line inside an added method may still arm against, or the
        /// refusal when none may.
        /// </summary>
        internal static (SourcePausePointCompiledMethodSpan Span, PausePointResponse Refusal)
            ScopeAddedMethodLineToCompiledSpan(
                EnablePausePointSchema parameters,
                HotReloadAddedMethodAtLine addedMethod)
        {
            // Why a compiled span and not just --method: the filter matches short names across
            // types and rounds forward, so only a span holding the line proves the caller passed a
            // last-compiled-source line of that compiled method.
            SourcePausePointCompiledMethodSpan span = FindSpanContainingLineOrNull(
                SourcePausePointResolver.FindCompiledMethodSpans(parameters.File, parameters.Method),
                parameters.Line);
            if (span == null)
            {
                return (null, PausePointResolveFailureResponse.CreateAddedMethodRefusal(
                    parameters,
                    addedMethod.Label,
                    methodFilterIsAmbiguous: false));
            }

            // Why also refuse when the filter names the added method: an edited line of that added
            // method can fall inside a same-named compiled method's span by number alone, and then
            // nothing tells which of the two the caller meant.
            if (MethodFilterAlsoNamesAddedMethod(parameters.Method, addedMethod))
            {
                return (null, PausePointResolveFailureResponse.CreateAddedMethodRefusal(
                    parameters,
                    addedMethod.Label,
                    methodFilterIsAmbiguous: true));
            }

            return (span, null);
        }

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

        /// <summary>
        /// Reports whether the --method filter also names the added method that holds the line,
        /// which leaves the line's owner ambiguous between the edited and the compiled source.
        /// </summary>
        internal static bool MethodFilterAlsoNamesAddedMethod(string methodFilter, HotReloadAddedMethodAtLine addedMethod)
        {
            return SourcePausePointResolver.MethodMatchesFilter(
                methodFilter,
                addedMethod.MethodName,
                addedMethod.DeclaringTypeName,
                addedMethod.NestedOuterTypeName);
        }

        internal static bool IsLineInsideSpan(SourcePausePointCompiledMethodSpan span, int line)
        {
            return span.StartLine <= line && line <= span.EndLine;
        }
    }
}
